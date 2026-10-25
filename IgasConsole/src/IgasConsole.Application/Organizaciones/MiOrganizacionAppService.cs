using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Autorizacion;
using IgasConsole.BackgroundJobs;
using IgasConsole.Identity;
using IgasConsole.Invitaciones;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace IgasConsole.Organizaciones;

/// <summary>
/// Ver <see cref="IMiOrganizacionAppService"/>. Quién puede qué lo decide el
/// <see cref="Autorizador"/> (papel en la organización → <see cref="PermisosEnOrganizacion"/>).
///
/// <para>⚠️ Un administrador de organización es un cliente (nivel 0): la jerarquía global no le
/// deja alcanzar a nadie. Aquí no se usa esa regla sino la de la organización, y por eso solo
/// ve y toca a los CLIENTES de su organización: si alguien del equipo (o el Developer) fuera
/// miembro, para él no existe (no sale, y tocarlo da 404).</para>
/// </summary>
[Authorize]
public class MiOrganizacionAppService : IgasConsoleAppService, IMiOrganizacionAppService
{
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IRepository<Invitacion, Guid> _invitaciones;
    private readonly IIdentityUserRepository _usuarios;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly Autorizador _autorizador;
    private readonly GestorDeInvitaciones _gestor;
    private readonly IBackgroundJobManager _jobs;

    public MiOrganizacionAppService(
        IRepository<Organizacion, Guid> organizaciones,
        IRepository<Invitacion, Guid> invitaciones,
        IIdentityUserRepository usuarios,
        JerarquiaDeUsuarios jerarquia,
        Autorizador autorizador,
        GestorDeInvitaciones gestor,
        IBackgroundJobManager jobs)
    {
        _organizaciones = organizaciones;
        _invitaciones = invitaciones;
        _usuarios = usuarios;
        _jerarquia = jerarquia;
        _autorizador = autorizador;
        _gestor = gestor;
        _jobs = jobs;
    }

    public async Task<List<MiOrganizacionDto>> GetListAsync()
    {
        var m = await _autorizador.QuienMiraAsync();
        var ids = m.Organizaciones.Keys.ToList();
        var orgs = await AsyncExecuter.ToListAsync(
            (await _organizaciones.WithDetailsAsync(o => o.Miembros)).Where(o => ids.Contains(o.Id)).OrderBy(o => o.Nombre));

        var salida = new List<MiOrganizacionDto>();
        foreach (var o in orgs)
        {
            salida.Add(await DtoAsync(o));
        }

        return salida;
    }

    public async Task<MiOrganizacionDto> InvitarAsync(Guid organizacionId, InvitarAMiOrganizacionDto input)
    {
        var o = await GestionableAsync(organizacionId);
        var invitacion = await _gestor.CrearAsync(
            input.Email, IgasRoles.Usuario, o.Id, input.Rol, porAdministradorDeOrganizacion: true);
        await _jobs.EnqueueAsync(new EnviarInvitacionArgs { InvitacionId = invitacion.Id });
        return await DtoAsync(o);
    }

    public async Task<MiOrganizacionDto> CambiarRolAsync(Guid organizacionId, Guid userId, CambiarRolEnOrganizacionDto input)
    {
        var o = await GestionableAsync(organizacionId);
        await ExigirClienteDeLaOrganizacionAsync(o, userId);
        o.CambiarRol(userId, input.Rol);
        // ⚠️ Hija con clave compuesta: SaveChanges, no UpdateAsync (ver CLAUDE.md).
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await DtoAsync(o);
    }

    public async Task<MiOrganizacionDto> QuitarAsync(Guid organizacionId, Guid userId)
    {
        var o = await GestionableAsync(organizacionId);
        await ExigirClienteDeLaOrganizacionAsync(o, userId);
        o.QuitarMiembro(userId);
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await DtoAsync(o);
    }

    public async Task<MiOrganizacionDto> RevocarInvitacionAsync(Guid organizacionId, Guid invitacionId)
    {
        var o = await GestionableAsync(organizacionId);
        var invitacion = await _invitaciones.FirstOrDefaultAsync(i => i.Id == invitacionId && i.OrganizacionId == o.Id)
                         ?? throw new EntityNotFoundException(typeof(Invitacion), invitacionId);
        invitacion.Revocar(Clock.Now);
        await _invitaciones.UpdateAsync(invitacion, autoSave: true);
        return await DtoAsync(o);
    }

    // --------------------------------------------------------------------------------

    /// <summary>La organización, si quien llama puede gestionar a su gente; si no, 404.</summary>
    private async Task<Organizacion> GestionableAsync(Guid organizacionId)
    {
        await _autorizador.ExigirAsync(organizacionId, AccionEnOrganizacion.GestionarPersonas);
        return await AsyncExecuter.FirstOrDefaultAsync(
                   (await _organizaciones.WithDetailsAsync(o => o.Miembros)).Where(o => o.Id == organizacionId))
               ?? throw new EntityNotFoundException(typeof(Organizacion), organizacionId);
    }

    /// <summary>Solo los CLIENTES de esa organización: el equipo (y el Developer) no existen aquí.</summary>
    private async Task ExigirClienteDeLaOrganizacionAsync(Organizacion o, Guid userId)
    {
        if (o.Miembros.All(x => x.UserId != userId) || !(await ClientesAsync([userId])).ContainsKey(userId))
        {
            throw new EntityNotFoundException(typeof(IdentityUser), userId);
        }
    }

    /// <summary>De estos usuarios, los que son clientes (nivel Usuario), con sus datos.</summary>
    private async Task<Dictionary<Guid, IdentityUser>> ClientesAsync(List<Guid> ids)
    {
        var salida = new Dictionary<Guid, IdentityUser>();
        if (ids.Count == 0)
        {
            return salida;
        }

        foreach (var u in await _usuarios.GetListByIdsAsync(ids))
        {
            if (await _jerarquia.NivelDeAsync(u) <= IgasRoles.NivelUsuario)
            {
                salida[u.Id] = u;
            }
        }

        return salida;
    }

    private async Task<MiOrganizacionDto> DtoAsync(Organizacion o)
    {
        var m = await _autorizador.QuienMiraAsync();
        var dto = new MiOrganizacionDto
        {
            Id = o.Id,
            Nombre = o.Nombre,
            MiRol = o.RolDe(m.Yo) ?? RolEnOrganizacion.Lector,
            PuedoGestionarPersonas = m.Puede(o.Id, AccionEnOrganizacion.GestionarPersonas),
        };

        if (!dto.PuedoGestionarPersonas)
        {
            return dto;
        }

        var clientes = await ClientesAsync(o.Miembros.Select(x => x.UserId).ToList());
        dto.Miembros = o.Miembros
            .Where(x => clientes.ContainsKey(x.UserId))
            .Select(x =>
            {
                var u = clientes[x.UserId];
                return new MiembroDto
                {
                    UserId = u.Id,
                    UserName = u.UserName,
                    NombreCompleto = string.IsNullOrWhiteSpace($"{u.Name} {u.Surname}") ? null : $"{u.Name} {u.Surname}".Trim(),
                    Email = u.Email,
                    Rol = x.Rol,
                };
            })
            .OrderByDescending(x => x.Rol).ThenBy(x => x.NombreCompleto ?? x.UserName)
            .ToList();

        var ahora = Clock.Now;
        var abiertas = await _invitaciones.GetListAsync(i => i.OrganizacionId == o.Id && i.AceptadaEn == null && i.RevocadaEn == null);
        dto.Pendientes = abiertas
            .Where(i => i.Estado(ahora) == EstadoInvitacion.Pendiente)
            .OrderByDescending(i => i.CreationTime)
            .Select(i => new InvitacionPendienteDto { Id = i.Id, Email = i.Email, Rol = i.RolEnOrganizacion, CreationTime = i.CreationTime })
            .ToList();
        return dto;
    }
}
