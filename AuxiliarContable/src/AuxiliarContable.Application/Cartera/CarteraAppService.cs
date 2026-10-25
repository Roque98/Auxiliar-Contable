using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Cfdis;
using AuxiliarContable.Conciliaciones;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.Documentos;
using AuxiliarContable.EstadosDeCuenta;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using AuxiliarContable.PerfilesFiscales;
using AuxiliarContable.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Cartera;

/// <summary>
/// Ver <see cref="ICarteraAppService"/>. Consultas por tabla agrupadas por cliente (no una por
/// cliente): con decenas de clientes son siete consultas en total.
/// </summary>
[Authorize(AuxiliarContablePermissions.Organizaciones.Default)]
public class CarteraAppService : AuxiliarContableAppService, ICarteraAppService
{
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<Cfdi, Guid> _cfdis;
    private readonly IRepository<EstadoDeCuenta, Guid> _estados;
    private readonly IRepository<MovimientoBancario, Guid> _movimientos;
    private readonly IRepository<Conciliacion, Guid> _conciliaciones;
    private readonly IRepository<MensajeDeConversacion, Guid> _mensajes;
    private readonly IRepository<Conversacion, Guid> _conversaciones;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly Autorizador _autorizador;

    public CarteraAppService(
        IRepository<Organizacion, Guid> organizaciones,
        IRepository<PerfilFiscal, Guid> perfiles,
        IRepository<Documento, Guid> documentos,
        IRepository<Cfdi, Guid> cfdis,
        IRepository<EstadoDeCuenta, Guid> estados,
        IRepository<MovimientoBancario, Guid> movimientos,
        IRepository<Conciliacion, Guid> conciliaciones,
        IRepository<MensajeDeConversacion, Guid> mensajes,
        IRepository<Conversacion, Guid> conversaciones,
        JerarquiaDeUsuarios jerarquia,
        Autorizador autorizador)
    {
        _organizaciones = organizaciones;
        _perfiles = perfiles;
        _documentos = documentos;
        _cfdis = cfdis;
        _estados = estados;
        _movimientos = movimientos;
        _conciliaciones = conciliaciones;
        _mensajes = mensajes;
        _conversaciones = conversaciones;
        _jerarquia = jerarquia;
        _autorizador = autorizador;
    }

    public async Task<List<ClienteDeCarteraDto>> GetClientesAsync(int anio, int mes)
    {
        // Además del permiso del atributo: la cartera es TODOS los clientes, así que se pregunta al
        // único punto de decisión (Autorizador) si quien llama es del equipo.
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("La cartera es solo para el equipo del despacho.");
        }

        var (inicio, fin) = Mes(anio, mes);

        var clientes = await AsyncExecuter.ToListAsync((await _organizaciones.GetQueryableAsync())
            .OrderBy(o => o.Nombre).Select(o => new { o.Id, o.Nombre }));
        var perfiles = (await _perfiles.GetListAsync()).ToDictionary(p => p.OrganizacionId);

        var documentos = await AsyncExecuter.ToListAsync((await _documentos.GetQueryableAsync())
            .Select(d => new { d.OrganizacionId, d.Tipo, Fecha = d.FechaDelDocumento ?? d.CreationTime, d.CreationTime }));
        var estados = await AsyncExecuter.ToListAsync((await _estados.GetQueryableAsync())
            .Select(e => new { e.Id, e.OrganizacionId, e.Revision, e.PeriodoFin }));
        var propuestas = await AsyncExecuter.ToListAsync((await _conciliaciones.GetQueryableAsync())
            .Where(c => c.Estado == EstadoDeConciliacion.Propuesta).Select(c => c.OrganizacionId));
        var sinFactura = await MovimientosSinFacturaAsync(inicio, fin);

        var total = CamposDelPerfil.Todos.Count;
        var lista = new List<ClienteDeCarteraDto>();
        foreach (var c in clientes)
        {
            var perfil = perfiles.GetValueOrDefault(c.Id);
            var susDocs = documentos.Where(d => d.OrganizacionId == c.Id).ToList();
            var delMes = susDocs.Where(d => d.Fecha >= inicio && d.Fecha < fin).ToList();
            var susEstados = estados.Where(e => e.OrganizacionId == c.Id).ToList();

            var dto = new ClienteDeCarteraDto
            {
                Id = c.Id,
                Nombre = c.Nombre,
                Rfc = perfil?.Dato(CamposDelPerfil.Rfc)?.Valor,
                PerfilCompleto = perfil == null ? 0 : (int)Math.Round(100.0 * perfil.Datos.Count / total),
                DatosPorConfirmar = perfil?.Datos.Count(d => !d.Confirmado) ?? 0,
                DocumentosDelMes = delMes.Count,
                FacturasDelMes = delMes.Count(d => d.Tipo == TipoDeDocumento.Cfdi),
                EstadosDeCuentaDelMes = delMes.Count(d => d.Tipo == TipoDeDocumento.EstadoDeCuenta),
                EstadosPorRevisar = susEstados.Count(e => e.Revision == RevisionDelEstadoDeCuenta.RequiereRevision),
                PropuestasPorRevisar = propuestas.Count(o => o == c.Id),
                MovimientosSinFactura = sinFactura.GetValueOrDefault(c.Id),
                UltimoDocumentoUtc = susDocs.Count == 0 ? null : susDocs.Max(d => d.CreationTime),
            };

            if (dto.Rfc == null)
            {
                dto.Pendientes.Add("Sin RFC: falta su constancia o su perfil.");
            }
            else if (dto.PerfilCompleto < 80)
            {
                dto.Pendientes.Add($"Perfil al {dto.PerfilCompleto} %.");
            }

            if (dto.EstadosDeCuentaDelMes == 0)
            {
                dto.Pendientes.Add("Falta el estado de cuenta del mes.");
            }

            if (dto.FacturasDelMes == 0)
            {
                dto.Pendientes.Add("No hay facturas del mes.");
            }

            if (dto.EstadosPorRevisar > 0)
            {
                dto.Pendientes.Add($"{dto.EstadosPorRevisar} estado(s) de cuenta no cuadran.");
            }

            if (dto.MovimientosSinFactura > 0)
            {
                dto.Pendientes.Add($"{dto.MovimientosSinFactura} movimiento(s) sin factura.");
            }

            if (dto.PropuestasPorRevisar > 0)
            {
                dto.Pendientes.Add($"{dto.PropuestasPorRevisar} pareja(s) por confirmar.");
            }

            dto.Semaforo = dto.Rfc == null || dto.EstadosPorRevisar > 0 || dto.EstadosDeCuentaDelMes == 0
                ? SemaforoDeCliente.Urgente
                : dto.Pendientes.Count > 0 ? SemaforoDeCliente.Atencion : SemaforoDeCliente.AlDia;
            lista.Add(dto);
        }

        return lista.OrderByDescending(c => c.Semaforo).ThenBy(c => c.Nombre).ToList();
    }

    public async Task<MetricasDelDespachoDto> GetMetricasAsync(int anio, int mes)
    {
        if (await _jerarquia.NivelActualAsync() < AuxiliarRoles.NivelOwner)
        {
            throw new AbpAuthorizationException("Las métricas del despacho son solo para el Owner.");
        }

        var (inicio, fin) = Mes(anio, mes);
        var clientes = await GetClientesAsync(anio, mes);
        var conciliados = await AsyncExecuter.CountAsync((await _conciliaciones.GetQueryableAsync())
            .Where(c => c.Estado == EstadoDeConciliacion.Confirmada
                        && (c.LastModificationTime ?? c.CreationTime) >= inicio
                        && (c.LastModificationTime ?? c.CreationTime) < fin));

        var chat = await AsyncExecuter.ToListAsync(
            from m in await _mensajes.GetQueryableAsync()
            join c in await _conversaciones.GetQueryableAsync() on m.ConversacionId equals c.Id
            where m.CreadoUtc >= inicio && m.CreadoUtc < fin
            select new { m.Rol, m.Tokens, c.UsuarioId });

        return new MetricasDelDespachoDto
        {
            Anio = anio,
            Mes = mes,
            Clientes = clientes.Count,
            ClientesConPerfilCompleto = clientes.Count(c => c.PerfilCompleto >= 80 && c.Rfc != null),
            ClientesAlDia = clientes.Count(c => c.Semaforo == SemaforoDeCliente.AlDia),
            DocumentosDelMes = clientes.Sum(c => c.DocumentosDelMes),
            FacturasDelMes = clientes.Sum(c => c.FacturasDelMes),
            EstadosDeCuentaDelMes = clientes.Sum(c => c.EstadosDeCuentaDelMes),
            ConciliadosDelMes = conciliados,
            MovimientosSinFactura = clientes.Sum(c => c.MovimientosSinFactura),
            MensajesDelChat = chat.Count(m => m.Rol == RolDelMensaje.Usuario),
            PersonasEnElChat = chat.Select(m => m.UsuarioId).Distinct().Count(),
            TokensDelChat = chat.Sum(m => (long)m.Tokens),
        };
    }

    /// <summary>Por cliente: movimientos del mes sin unión viva ni justificación, de estados que cuadran.</summary>
    private async Task<Dictionary<Guid, int>> MovimientosSinFacturaAsync(DateTime inicio, DateTime fin)
    {
        var unidos = (await _conciliaciones.GetQueryableAsync())
            .Where(c => c.Estado != EstadoDeConciliacion.Rechazada).Select(c => c.MovimientoId);
        var consulta =
            from m in await _movimientos.GetQueryableAsync()
            join e in await _estados.GetQueryableAsync() on m.EstadoDeCuentaId equals e.Id
            where m.Fecha >= inicio && m.Fecha < fin
                  && e.Revision != RevisionDelEstadoDeCuenta.RequiereRevision
                  && m.SinFacturaPorque == null
                  && !unidos.Contains(m.Id)
            group m by m.OrganizacionId into g
            select new { OrganizacionId = g.Key, Cantidad = g.Count() };
        return (await AsyncExecuter.ToListAsync(consulta)).ToDictionary(x => x.OrganizacionId, x => x.Cantidad);
    }

    private static (DateTime Inicio, DateTime Fin) Mes(int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new Volo.Abp.UserFriendlyException("Mes inválido.");
        }

        var inicio = new DateTime(anio, mes, 1);
        return (inicio, inicio.AddMonths(1));
    }
}
