using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace AuxiliarContable.Contabilidad;

/// <summary>Ver <see cref="IConciliacionIntegralAppService"/>.</summary>
[Authorize]
public class ConciliacionIntegralAppService : AuxiliarContableAppService, IConciliacionIntegralAppService
{
    /// <summary>Una vuelta que lleva más que esto «trabajando» se da por caída y se puede pedir otra.</summary>
    public static readonly TimeSpan Atorada = TimeSpan.FromHours(2);

    private readonly IRepository<ConciliacionIntegral, Guid> _vueltas;
    private readonly Autorizador _autorizador;

    public ConciliacionIntegralAppService(IRepository<ConciliacionIntegral, Guid> vueltas, Autorizador autorizador)
    {
        _vueltas = vueltas;
        _autorizador = autorizador;
    }

    public async Task<ConciliacionIntegralDto> PedirAsync(Guid organizacionId, int anio, int mes)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Subir);
        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            throw new UserFriendlyException("Mes no válido.");
        }

        var hoy = Clock.Now;
        if (new DateTime(anio, mes, 1) > hoy)
        {
            throw new UserFriendlyException("Ese mes todavía no empieza.");
        }

        // Si ya hay una vuelta en curso del cliente, es la misma: no se encolan dos.
        var ahora = Clock.Now.ToUniversalTime();
        var enCurso = (await _vueltas.GetQueryableAsync())
            .Where(v => v.OrganizacionId == organizacionId
                        && (v.Estado == EstadoDeConciliacionIntegral.Pendiente || v.Estado == EstadoDeConciliacionIntegral.Trabajando))
            .OrderByDescending(v => v.PedidaUtc).FirstOrDefault();
        if (enCurso != null && ahora - enCurso.PedidaUtc < Atorada)
        {
            return Dto(enCurso);
        }

        var vuelta = new ConciliacionIntegral(GuidGenerator.Create(), organizacionId, anio, mes, CurrentUser.GetId(), ahora);
        await _vueltas.InsertAsync(vuelta, autoSave: true);
        return Dto(vuelta);
    }

    public async Task<ConciliacionIntegralDto?> GetUltimaAsync(Guid organizacionId)
    {
        await ExigirEquipoAsync(organizacionId, AccionEnOrganizacion.Ver);
        var v = (await _vueltas.GetQueryableAsync())
            .Where(x => x.OrganizacionId == organizacionId).OrderByDescending(x => x.PedidaUtc).FirstOrDefault();
        return v == null ? null : Dto(v);
    }

    private static ConciliacionIntegralDto Dto(ConciliacionIntegral v) => new()
    {
        Id = v.Id,
        Anio = v.Anio,
        Mes = v.Mes,
        Estado = v.Estado.ToString(),
        PedidaUtc = v.PedidaUtc,
        IniciadaUtc = v.IniciadaUtc,
        TerminadaUtc = v.TerminadaUtc,
        Pasos = Leer<List<PasoDeConciliacionDto>>(v.Pasos) ?? [],
        Meses = Leer<List<MesConciliadoDto>>(v.Resultado) ?? [],
        Error = v.Error,
    };

    private static T? Leer<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task ExigirEquipoAsync(Guid organizacionId, AccionEnOrganizacion accion)
    {
        await _autorizador.ExigirAsync(organizacionId, accion);
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("La conciliación es del despacho.");
        }
    }
}
