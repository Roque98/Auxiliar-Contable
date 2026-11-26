using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Autorizacion;
using AuxiliarContable.Contabilidad;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace AuxiliarContable.Conversaciones;

/// <summary>Ver <see cref="ISolicitudesAppService"/>.</summary>
[Authorize]
public class SolicitudesAppService : AuxiliarContableAppService, ISolicitudesAppService
{
    private readonly IRepository<Solicitud, Guid> _solicitudes;
    private readonly IRepository<Operacion, Guid> _operaciones;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly Autorizador _autorizador;

    public SolicitudesAppService(
        IRepository<Solicitud, Guid> solicitudes,
        IRepository<Operacion, Guid> operaciones,
        IRepository<Organizacion, Guid> organizaciones,
        Autorizador autorizador)
    {
        _solicitudes = solicitudes;
        _operaciones = operaciones;
        _organizaciones = organizaciones;
        _autorizador = autorizador;
    }

    public async Task<List<SolicitudDto>> GetListAsync(Guid organizacionId, bool soloPendientes = false)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var equipo = (await _autorizador.QuienMiraAsync()).Equipo;
        var q = (await _solicitudes.GetQueryableAsync()).Where(s => s.OrganizacionId == organizacionId);
        if (soloPendientes)
        {
            q = q.Where(s => s.Estado == EstadoDeSolicitud.Pendiente);
        }

        var lista = await AsyncExecuter.ToListAsync(q
            .OrderBy(s => s.Estado == EstadoDeSolicitud.Pendiente ? 0 : 1)
            .ThenByDescending(s => s.CreationTime)
            .Take(100));
        return [.. lista.Select(s => Dto(s, null, equipo))];
    }

    public async Task<SolicitudDto> CrearAsync(Guid organizacionId, CrearSolicitudDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var equipo = (await _autorizador.QuienMiraAsync()).Equipo;
        if (input.OperacionId is { } operacionId)
        {
            var o = await _operaciones.FindAsync(operacionId);
            if (o == null || o.OrganizacionId != organizacionId)
            {
                throw new EntityNotFoundException(typeof(Operacion), operacionId);
            }
        }

        var texto = (input.Texto ?? "").Trim();
        if (texto.Length < 5)
        {
            throw new UserFriendlyException("Escribe qué se pide, con un poco más de detalle.");
        }

        var solicitud = new Solicitud(GuidGenerator.Create(), organizacionId,
            equipo ? SentidoDeSolicitud.AlCliente : SentidoDeSolicitud.AlDespacho, input.Tipo, texto, input.OperacionId);
        await _solicitudes.InsertAsync(solicitud, autoSave: true);
        return Dto(solicitud, null, equipo);
    }

    public async Task<SolicitudDto> ResolverAsync(Guid organizacionId, Guid solicitudId, ResolverSolicitudDto input)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.Ver);
        var equipo = (await _autorizador.QuienMiraAsync()).Equipo;
        var solicitud = await _solicitudes.FindAsync(solicitudId);
        if (solicitud == null || solicitud.OrganizacionId != organizacionId)
        {
            throw new EntityNotFoundException(typeof(Solicitud), solicitudId);
        }

        if (!equipo && solicitud.Sentido == SentidoDeSolicitud.AlDespacho)
        {
            throw new AbpAuthorizationException("Lo que el cliente le pide al despacho solo lo atiende el despacho.");
        }

        solicitud.Resolver(input.Estado, input.Respuesta, CurrentUser.GetId(), Clock.Now.ToUniversalTime());
        await _solicitudes.UpdateAsync(solicitud, autoSave: true);
        return Dto(solicitud, null, equipo);
    }

    public async Task<List<SolicitudDto>> GetPendientesDelDespachoAsync()
    {
        if (!(await _autorizador.QuienMiraAsync()).Equipo)
        {
            throw new AbpAuthorizationException("La bandeja de solicitudes es del despacho.");
        }

        var lista = await AsyncExecuter.ToListAsync((await _solicitudes.GetQueryableAsync())
            .Where(s => s.Sentido == SentidoDeSolicitud.AlDespacho && s.Estado == EstadoDeSolicitud.Pendiente)
            .OrderBy(s => s.CreationTime)
            .Take(200));
        var ids = lista.Select(s => s.OrganizacionId).Distinct().ToList();
        var nombres = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _organizaciones.GetListAsync(o => ids.Contains(o.Id))).ToDictionary(o => o.Id, o => o.Nombre);
        return [.. lista.Select(s => Dto(s, nombres.GetValueOrDefault(s.OrganizacionId), equipo: true))];
    }

    /// <summary>Lo pedido al despacho, solo el despacho; lo pedido al cliente, el cliente (o el despacho, que lo pidió).</summary>
    private static bool PuedeResolver(Solicitud s, bool equipo) =>
        s.Estado == EstadoDeSolicitud.Pendiente && (equipo || s.Sentido == SentidoDeSolicitud.AlCliente);

    private static SolicitudDto Dto(Solicitud s, string? cliente, bool equipo) => new()
    {
        Id = s.Id,
        OrganizacionId = s.OrganizacionId,
        NombreDelCliente = cliente,
        Sentido = s.Sentido,
        Tipo = s.Tipo,
        Texto = s.Texto,
        OperacionId = s.OperacionId,
        Estado = s.Estado,
        Respuesta = s.Respuesta,
        CreationTime = s.CreationTime,
        AtendidaUtc = s.AtendidaUtc,
        PuedeResolver = PuedeResolver(s, equipo),
    };
}
