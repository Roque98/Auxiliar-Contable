using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.BackgroundJobs;
using AuxiliarContable.Identity;
using AuxiliarContable.Notifications;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AuxiliarContable.Invitaciones;

/// <summary>Ver <see cref="IInvitacionAppService"/>.</summary>
[Authorize(IdentityPermissions.Users.Create)]
public class InvitacionAppService : AuxiliarContableAppService, IInvitacionAppService
{
    private readonly IRepository<Invitacion, Guid> _invitaciones;
    private readonly GestorDeInvitaciones _gestor;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly IIdentityUserRepository _usuarios;
    private readonly IBackgroundJobManager _jobs;
    private readonly IEstadoDelCorreo _correo;
    private readonly IRepository<Organizacion, Guid> _organizaciones;

    public InvitacionAppService(
        IRepository<Invitacion, Guid> invitaciones,
        GestorDeInvitaciones gestor,
        JerarquiaDeUsuarios jerarquia,
        IIdentityUserRepository usuarios,
        IBackgroundJobManager jobs,
        IEstadoDelCorreo correo,
        IRepository<Organizacion, Guid> organizaciones)
    {
        _organizaciones = organizaciones;
        _invitaciones = invitaciones;
        _gestor = gestor;
        _jerarquia = jerarquia;
        _usuarios = usuarios;
        _jobs = jobs;
        _correo = correo;
    }

    public async Task<OpcionesDeInvitacionDto> GetOpcionesAsync()
    {
        var roles = await _jerarquia.RolesAlAlcanceAsync();
        var niveles = await _jerarquia.NivelesAsync();
        return new OpcionesDeInvitacionDto
        {
            CorreoConfigurado = _correo.PuedeEnviar,
            DiasDeVigencia = InvitacionConsts.DiasDeVigencia,
            // El Developer no se invita: es la cuenta de quien mantiene la consola, y una
            // invitación la haría aparecer en la lista de quien la reciba.
            Roles = roles
                .Where(r => AuxiliarRoles.Nivel(r.Name, niveles) < AuxiliarRoles.NivelDeveloper)
                .Select(r => new RolInvitableDto { Nombre = r.Name, Etiqueta = AuxiliarRoles.Etiqueta(r.Name), Nivel = AuxiliarRoles.Nivel(r.Name, niveles) })
                .OrderByDescending(r => r.Nivel)
                .ThenBy(r => r.Etiqueta)
                .ToList()
        };
    }

    public async Task<PagedResultDto<InvitacionDto>> GetListAsync(GetInvitacionesInput input)
    {
        var consulta = await ConsultaVisibleAsync();
        var ahora = Clock.Now;

        if (!string.IsNullOrWhiteSpace(input.Filtro))
        {
            var filtro = input.Filtro.Trim().ToLowerInvariant();
            consulta = consulta.Where(i => i.Email.Contains(filtro));
        }

        // El estado se deduce de las fechas, así que el filtro repite esa misma lógica.
        consulta = input.Estado switch
        {
            EstadoInvitacion.Aceptada => consulta.Where(i => i.AceptadaEn != null),
            EstadoInvitacion.Revocada => consulta.Where(i => i.AceptadaEn == null && i.RevocadaEn != null),
            EstadoInvitacion.Vencida => consulta.Where(i => i.AceptadaEn == null && i.RevocadaEn == null && i.ExpiraEn != null && i.ExpiraEn <= ahora),
            EstadoInvitacion.Pendiente => consulta.Where(i => i.AceptadaEn == null && i.RevocadaEn == null && (i.ExpiraEn == null || i.ExpiraEn > ahora)),
            _ => consulta
        };

        var total = await AsyncExecuter.CountAsync(consulta);
        var pagina = await AsyncExecuter.ToListAsync(
            consulta.OrderByDescending(i => i.CreationTime).ThenBy(i => i.Id)
                .Skip(input.SkipCount).Take(input.MaxResultCount));

        var autores = await AutoresAsync(pagina);
        var nombres = await OrganizacionesAsync(pagina);
        return new PagedResultDto<InvitacionDto>(total, pagina.Select(i => Mapear(i, autores, nombres)).ToList());
    }

    public async Task<InvitacionDto> CreateAsync(CrearInvitacionDto input)
    {
        var invitacion = await _gestor.CrearAsync(input.Email, input.Rol, input.OrganizacionId, input.RolEnOrganizacion);
        await _jobs.EnqueueAsync(new EnviarInvitacionArgs { InvitacionId = invitacion.Id });
        return await MapearAsync(invitacion);
    }

    public async Task<InvitacionDto> ReenviarAsync(Guid id)
    {
        var invitacion = await ObtenerVisibleAsync(id);
        _gestor.ExigirReenviable(invitacion);
        await _jobs.EnqueueAsync(new EnviarInvitacionArgs { InvitacionId = invitacion.Id });
        return await MapearAsync(invitacion);
    }

    public async Task<InvitacionDto> RevocarAsync(Guid id)
    {
        var invitacion = await ObtenerVisibleAsync(id);
        invitacion.Revocar(Clock.Now);
        await _invitaciones.UpdateAsync(invitacion, autoSave: true);
        return await MapearAsync(invitacion);
    }

    /// <summary>
    /// Las que quien pregunta alcanza: el rol invitado está por debajo de él y quien invitó
    /// no está fuera de su alcance (si no, la lista delataría a la cuenta Developer).
    /// </summary>
    private async Task<IQueryable<Invitacion>> ConsultaVisibleAsync()
    {
        var roles = (await _jerarquia.RolesAlAlcanceAsync()).Select(r => r.Name).ToList();
        var ocultos = await _jerarquia.UsuariosFueraDeAlcanceAsync();

        return (await _invitaciones.GetQueryableAsync())
            .Where(i => roles.Contains(i.Rol))
            .Where(i => i.CreatorId == null || !ocultos.Contains(i.CreatorId.Value));
    }

    private async Task<Invitacion> ObtenerVisibleAsync(Guid id)
    {
        var consulta = (await ConsultaVisibleAsync()).Where(i => i.Id == id);
        return await AsyncExecuter.FirstOrDefaultAsync(consulta)
               ?? throw new EntityNotFoundException(typeof(Invitacion), id);
    }

    private async Task<Dictionary<Guid, string>> AutoresAsync(IEnumerable<Invitacion> lista)
    {
        var ids = lista.Where(i => i.CreatorId.HasValue).Select(i => i.CreatorId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return (await _usuarios.GetListByIdsAsync(ids)).ToDictionary(u => u.Id, u => u.UserName);
    }

    private async Task<InvitacionDto> MapearAsync(Invitacion i) =>
        Mapear(i, await AutoresAsync([i]), await OrganizacionesAsync([i]));

    /// <summary>Nombre de cada organización citada (las borradas no salen: filtro de ABP).</summary>
    private async Task<Dictionary<Guid, string>> OrganizacionesAsync(IEnumerable<Invitacion> lista)
    {
        var ids = lista.Where(i => i.OrganizacionId.HasValue).Select(i => i.OrganizacionId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return (await _organizaciones.GetListAsync(o => ids.Contains(o.Id))).ToDictionary(o => o.Id, o => o.Nombre);
    }

    private InvitacionDto Mapear(Invitacion i, IReadOnlyDictionary<Guid, string> autores, IReadOnlyDictionary<Guid, string> organizaciones)
    {
        var estado = i.Estado(Clock.Now);
        return new InvitacionDto
        {
            Id = i.Id,
            Email = i.Email,
            Rol = i.Rol,
            RolEtiqueta = AuxiliarRoles.Etiqueta(i.Rol),
            Estado = estado,
            CreationTime = i.CreationTime,
            ExpiraEn = i.ExpiraEn,
            UltimoEnvio = i.UltimoEnvio,
            Envios = i.Envios,
            AceptadaEn = i.AceptadaEn,
            EnCola = estado == EstadoInvitacion.Pendiente && i.Envios == 0,
            InvitadoPor = i.CreatorId.HasValue && autores.TryGetValue(i.CreatorId.Value, out var nombre) ? nombre : null,
            OrganizacionId = i.OrganizacionId,
            Organizacion = i.OrganizacionId.HasValue && organizaciones.TryGetValue(i.OrganizacionId.Value, out var org) ? org : null,
        };
    }
}
