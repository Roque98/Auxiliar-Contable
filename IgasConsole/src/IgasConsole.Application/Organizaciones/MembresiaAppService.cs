using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Identity;
using IgasConsole.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace IgasConsole.Organizaciones;

/// <summary>Ver <see cref="IMembresiaAppService"/>.</summary>
[Authorize(IdentityPermissions.Users.Default)]
public class MembresiaAppService : IgasConsoleAppService, IMembresiaAppService
{
    /// <summary>Techo de ids por petición: la pantalla pide los de una página.</summary>
    private const int MaxUsuarios = 200;

    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly JerarquiaDeUsuarios _jerarquia;

    public MembresiaAppService(IRepository<Organizacion, Guid> organizaciones, JerarquiaDeUsuarios jerarquia)
    {
        _organizaciones = organizaciones;
        _jerarquia = jerarquia;
    }

    public async Task<OpcionesDeOrganizacionDto> GetOpcionesAsync()
    {
        var lista = await AsyncExecuter.ToListAsync(
            (await _organizaciones.GetQueryableAsync()).OrderBy(o => o.Nombre));

        return new OpcionesDeOrganizacionDto
        {
            Organizaciones = lista.Select(o => new OrganizacionResumenDto { Id = o.Id, Nombre = o.Nombre }).ToList(),
            PuedeCrear = await AuthorizationService.IsGrantedAsync(IgasConsolePermissions.Organizaciones.Default),
            NivelSinOrganizacion = IgasRoles.NivelUsuario + 1,
        };
    }

    public async Task<List<MembresiaDeUsuarioDto>> GetDeUsuariosAsync(List<Guid> userIds)
    {
        // Nadie fuera de alcance: preguntar por el Developer no debe decir nada de él.
        var ocultos = await _jerarquia.UsuariosFueraDeAlcanceAsync();
        var ids = (userIds ?? []).Distinct().Where(id => !ocultos.Contains(id)).Take(MaxUsuarios).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var filas = await AsyncExecuter.ToListAsync(
            (await _organizaciones.GetQueryableAsync())
                .SelectMany(o => o.Miembros.Where(m => ids.Contains(m.UserId)).Select(m => new { m.UserId, o.Id, o.Nombre })));

        return filas
            .GroupBy(f => f.UserId)
            .Select(g => new MembresiaDeUsuarioDto
            {
                UserId = g.Key,
                Organizaciones = g.OrderBy(f => f.Nombre).Select(f => new OrganizacionResumenDto { Id = f.Id, Nombre = f.Nombre }).ToList(),
            })
            .ToList();
    }
}
