using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Conversaciones;
using AuxiliarContable.Documentos;
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
/// cliente). El semáforo solo mira lo que esta aplicación sabe: perfil y documentos recibidos.
/// Lo que sale de leerlos (estados que no cuadran, movimientos sin factura) lo dará el motor.
/// </summary>
[Authorize(AuxiliarContablePermissions.Organizaciones.Default)]
public class CarteraAppService : AuxiliarContableAppService, ICarteraAppService
{
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IRepository<PerfilFiscal, Guid> _perfiles;
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<MensajeDeConversacion, Guid> _mensajes;
    private readonly IRepository<Conversacion, Guid> _conversaciones;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly Autorizador _autorizador;

    public CarteraAppService(
        IRepository<Organizacion, Guid> organizaciones,
        IRepository<PerfilFiscal, Guid> perfiles,
        IRepository<Documento, Guid> documentos,
        IRepository<MensajeDeConversacion, Guid> mensajes,
        IRepository<Conversacion, Guid> conversaciones,
        JerarquiaDeUsuarios jerarquia,
        Autorizador autorizador)
    {
        _organizaciones = organizaciones;
        _perfiles = perfiles;
        _documentos = documentos;
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

        var total = CamposDelPerfil.Todos.Count;
        var lista = new List<ClienteDeCarteraDto>();
        foreach (var c in clientes)
        {
            var perfil = perfiles.GetValueOrDefault(c.Id);
            var susDocs = documentos.Where(d => d.OrganizacionId == c.Id).ToList();
            var delMes = susDocs.Where(d => d.Fecha >= inicio && d.Fecha < fin).ToList();

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

            dto.Semaforo = dto.Rfc == null || dto.EstadosDeCuentaDelMes == 0
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
            MensajesDelChat = chat.Count(m => m.Rol == RolDelMensaje.Usuario),
            PersonasEnElChat = chat.Select(m => m.UsuarioId).Distinct().Count(),
            TokensDelChat = chat.Sum(m => (long)m.Tokens),
        };
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
