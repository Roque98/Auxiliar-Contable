using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Bancos;
using AuxiliarContable.Documentos;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AuxiliarContable.Contabilidad;

/// <summary>Ver <see cref="ICierreAppService"/>. Lo ajeno = 404, como en la bóveda.</summary>
[Authorize]
public class CierreAppService : AuxiliarContableAppService, ICierreAppService
{
    private readonly IRepository<Documento, Guid> _documentos;
    private readonly IRepository<PeriodoContable, Guid> _periodos;
    private readonly IRepository<ConciliacionBancaria, Guid> _conciliaciones;
    private readonly IRepository<PartidaPendiente, Guid> _partidas;
    private readonly Autorizador _autorizador;

    public CierreAppService(
        IRepository<Documento, Guid> documentos,
        IRepository<PeriodoContable, Guid> periodos,
        IRepository<ConciliacionBancaria, Guid> conciliaciones,
        IRepository<PartidaPendiente, Guid> partidas,
        Autorizador autorizador)
    {
        _documentos = documentos;
        _periodos = periodos;
        _conciliaciones = conciliaciones;
        _partidas = partidas;
        _autorizador = autorizador;
    }

    public async Task<CierreDelMesDto> GetAsync(Guid organizacionId, int anio, int mes)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        ValidarMes(anio, mes);
        return await ArmarAsync(organizacionId, anio, mes);
    }

    public async Task<List<PartidaDto>> GetPreguntasAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var preguntas = await _partidas.GetListAsync(p => p.OrganizacionId == organizacionId
                                                         && p.Resolucion == ResolucionDePartida.PreguntarAlCliente
                                                         && p.Respuesta == null);
        return preguntas.OrderBy(p => p.Anio).ThenBy(p => p.Mes).ThenBy(p => p.Descripcion).Select(Describir).ToList();
    }

    public async Task<PartidaDto> ResolverAsync(Guid organizacionId, Guid partidaId, ResolverPartidaDto input)
    {
        await ExigirDespachoAsync(organizacionId);
        var partida = await PartidaAsync(organizacionId, partidaId);
        await ExigirAbiertoAsync(organizacionId, partida.Anio, partida.Mes);
        if (!Enum.IsDefined(input.Resolucion))
        {
            throw new UserFriendlyException("Resolución no válida.");
        }

        if (input.Resolucion == ResolucionDePartida.PreguntarAlCliente && string.IsNullOrWhiteSpace(input.Nota))
        {
            throw new UserFriendlyException("Escribe qué le quieres preguntar al cliente.");
        }

        partida.Resolver(input.Resolucion, input.Nota);
        await _partidas.UpdateAsync(partida, autoSave: true);
        return Describir(partida);
    }

    public async Task<PartidaDto> ResponderAsync(Guid organizacionId, Guid partidaId, ResponderPartidaDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Subir);
        var partida = await PartidaAsync(organizacionId, partidaId);
        await ExigirAbiertoAsync(organizacionId, partida.Anio, partida.Mes);
        if (string.IsNullOrWhiteSpace(input.Respuesta))
        {
            throw new UserFriendlyException("Escribe qué es ese movimiento.");
        }

        partida.Responder(input.Respuesta, Clock.Now);
        await _partidas.UpdateAsync(partida, autoSave: true);
        return Describir(partida);
    }

    public async Task<CierreDelMesDto> CerrarAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirDespachoAsync(organizacionId);
        ValidarMes(anio, mes);
        var cierre = await ArmarAsync(organizacionId, anio, mes);
        if (cierre.Etapa == EtapaDelPeriodo.Cerrado)
        {
            return cierre;
        }

        if (cierre.Etapa != EtapaDelPeriodo.ListoParaCerrar)
        {
            var falta = cierre.Revision.FirstOrDefault(r => r.Bloquea && !r.Hecho)?.Texto;
            throw new UserFriendlyException($"Todavía no se puede cerrar: {falta}");
        }

        var periodo = await _periodos.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes);
        if (periodo == null)
        {
            periodo = new PeriodoContable(GuidGenerator.Create(), organizacionId, anio, mes);
            periodo.Cerrar(CurrentUser.Id, Clock.Now);
            await _periodos.InsertAsync(periodo, autoSave: true);
        }
        else
        {
            periodo.Cerrar(CurrentUser.Id, Clock.Now);
            await _periodos.UpdateAsync(periodo, autoSave: true);
        }

        return await ArmarAsync(organizacionId, anio, mes);
    }

    public async Task<CierreDelMesDto> ReabrirAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirDespachoAsync(organizacionId);
        ValidarMes(anio, mes);
        var periodo = await _periodos.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes);
        if (periodo is { Cerrado: true })
        {
            periodo.Reabrir();
            await _periodos.UpdateAsync(periodo, autoSave: true);
        }

        return await ArmarAsync(organizacionId, anio, mes);
    }

    private async Task<CierreDelMesDto> ArmarAsync(Guid organizacionId, int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddMonths(1);
        var delMes = await AsyncExecuter.ToListAsync(
            from d in await _documentos.GetQueryableAsync()
            where d.OrganizacionId == organizacionId
                  && (d.Tipo == TipoDeDocumento.EstadoDeCuenta || d.Tipo == TipoDeDocumento.Cfdi)
                  && (d.FechaDelDocumento ?? d.CreationTime) >= desde
                  && (d.FechaDelDocumento ?? d.CreationTime) < hasta
            select new { d.Id, d.Sha256, d.Tipo });

        var periodo = await _periodos.FirstOrDefaultAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes);
        var conciliacion = await _conciliaciones.FirstOrDefaultAsync(c => c.OrganizacionId == organizacionId && c.Anio == anio && c.Mes == mes);
        var partidas = await _partidas.GetListAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes);

        var estados = delMes.Count(d => d.Tipo == TipoDeDocumento.EstadoDeCuenta);
        var facturas = delMes.Count(d => d.Tipo == TipoDeDocumento.Cfdi);
        var huella = GestorDePeriodos.HuellaDe(delMes.Select(d => (d.Id, d.Sha256)));
        var cerrado = periodo?.Cerrado == true;
        var pendientes = partidas.Count(p => p.Pendiente);
        // Cerrado, los archivos ya no cambian: la conciliación con que se cerró es la que vale.
        var desactualizada = conciliacion != null && conciliacion.Huella != huella && !cerrado;
        var resultado = conciliacion == null ? null : GestorDeConciliaciones.Leer(conciliacion);

        var revision = new List<RenglonDelCierreDto>
        {
            new() { Texto = estados == 0 ? "Falta el estado de cuenta del mes." : $"Estados de cuenta del mes: {estados}.", Hecho = estados > 0, Bloquea = true },
            new() { Texto = facturas == 0 ? "No hay facturas del mes (¿ya se descargaron del SAT?)." : $"Facturas del mes: {facturas}.", Hecho = facturas > 0 },
            new()
            {
                Texto = conciliacion == null ? "Falta conciliar los bancos."
                    : desactualizada ? "Cambiaron los documentos después de conciliar: hay que volver a conciliar."
                    : "Bancos conciliados con los documentos de hoy.",
                Hecho = conciliacion != null && !desactualizada,
                Bloquea = true,
            },
        };

        if (resultado != null)
        {
            var noCuadran = resultado.Cuentas.Where(c => !c.Cuadra).Select(c => $"{c.Banco} {c.Cuenta}".Trim()).ToList();
            revision.Add(new RenglonDelCierreDto
            {
                Texto = noCuadran.Count == 0 ? "Los estados de cuenta cuadran con lo que imprime el banco."
                    : $"No cuadra con lo que imprime el banco: {string.Join(", ", noCuadran)}. Revisa la lectura.",
                Hecho = noCuadran.Count == 0,
            });
            revision.Add(new RenglonDelCierreDto
            {
                Texto = pendientes == 0 ? "Todos los movimientos tienen factura o explicación."
                    : $"Movimientos sin factura por resolver: {pendientes}.",
                Hecho = pendientes == 0,
                Bloquea = true,
            });
            if (resultado.FacturasSinMovimiento.Count > 0)
            {
                revision.Add(new RenglonDelCierreDto
                {
                    Texto = $"Facturas sin movimiento en el banco: {resultado.FacturasSinMovimiento.Count} (a crédito, en efectivo o de otra cuenta).",
                });
            }
        }

        return new CierreDelMesDto
        {
            Anio = anio,
            Mes = mes,
            Etapa = EtapaDelCierre.Calcular(cerrado, estados, huella, conciliacion?.Huella, pendientes),
            Revision = revision,
            EstadosDeCuenta = estados,
            Facturas = facturas,
            Conciliacion = conciliacion == null ? null : BancosAppService.Describir(conciliacion, resultado),
            ConciliacionDesactualizada = desactualizada,
            Partidas = partidas.OrderBy(p => p.Descripcion).Select(Describir).ToList(),
            CerradoUtc = cerrado ? periodo!.CerradoUtc : null,
            PuedeCerrar = (await _autorizador.QuienMiraAsync()).Equipo,
        };
    }

    /// <summary>Resolver, cerrar y reabrir es trabajo del despacho, no del cliente.</summary>
    private async Task ExigirDespachoAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("Eso lo hace el despacho.");
        }
    }

    private async Task ExigirAbiertoAsync(Guid organizacionId, int anio, int mes)
    {
        if (await _periodos.AnyAsync(p => p.OrganizacionId == organizacionId && p.Anio == anio && p.Mes == mes && p.Cerrado))
        {
            throw new UserFriendlyException($"El periodo {mes:00}/{anio} ya está cerrado.");
        }
    }

    private async Task<PartidaPendiente> PartidaAsync(Guid organizacionId, Guid partidaId)
    {
        var partida = await _partidas.FindAsync(partidaId);
        if (partida == null || partida.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(PartidaPendiente), partidaId);
        }

        return partida;
    }

    private static void ValidarMes(int anio, int mes)
    {
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }
    }

    private static PartidaDto Describir(PartidaPendiente p) => new()
    {
        Id = p.Id,
        Anio = p.Anio,
        Mes = p.Mes,
        Clave = p.Clave,
        Descripcion = p.Descripcion,
        Resolucion = p.Resolucion,
        Nota = p.Nota,
        Respuesta = p.Respuesta,
        RespondidaUtc = p.RespondidaUtc,
        Pendiente = p.Pendiente,
    };
}
