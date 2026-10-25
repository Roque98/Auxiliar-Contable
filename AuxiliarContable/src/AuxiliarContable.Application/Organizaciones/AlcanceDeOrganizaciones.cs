using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace AuxiliarContable.Organizaciones;

/// <summary>
/// QUIÉN VE QUÉ clientes:
///
/// <list type="bullet">
/// <item>El equipo del despacho (<see cref="AuxiliarContablePermissions.Organizaciones.Default"/>:
/// Owner y Administrador): todos los clientes.</item>
/// <item>La gente de un cliente: solo las organizaciones de las que es miembro.</item>
/// <item>El Developer: todo. Lo suyo no lo ve nadie más.</item>
/// </list>
///
/// <para><see cref="VeTodasAsync"/> es virtual para los tests: allí la autorización está en
/// «todo permitido» y sin sustituirla no se podría probar a un cliente.</para>
/// </summary>
public class AlcanceDeOrganizaciones : ITransientDependency
{
    private readonly IPermissionChecker _permisos;
    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IAsyncQueryableExecuter _ejecutor;

    public AlcanceDeOrganizaciones(
        IPermissionChecker permisos,
        IRepository<Organizacion, Guid> organizaciones,
        IAsyncQueryableExecuter ejecutor)
    {
        _permisos = permisos;
        _organizaciones = organizaciones;
        _ejecutor = ejecutor;
    }

    public virtual Task<bool> VeTodasAsync() => _permisos.IsGrantedAsync(AuxiliarContablePermissions.Organizaciones.Default);

    /// <summary>Las organizaciones de las que <paramref name="userId"/> es miembro.</summary>
    public async Task<List<Organizacion>> OrganizacionesDeAsync(Guid userId)
    {
        var consulta = (await _organizaciones.GetQueryableAsync())
            .Where(o => o.Miembros.Any(m => m.UserId == userId))
            .OrderBy(o => o.Nombre);
        return await _ejecutor.ToListAsync(consulta);
    }

    /// <summary>De qué organizaciones es miembro <paramref name="userId"/> y con qué papel en cada una.</summary>
    public async Task<IReadOnlyDictionary<Guid, RolEnOrganizacion>> MembresiasDeAsync(Guid userId)
    {
        var consulta = (await _organizaciones.GetQueryableAsync())
            .SelectMany(o => o.Miembros.Where(m => m.UserId == userId).Select(m => new { o.Id, m.Rol }));
        return (await _ejecutor.ToListAsync(consulta)).ToDictionary(x => x.Id, x => x.Rol);
    }

    /// <summary>Los ids de las organizaciones de las que <paramref name="userId"/> es miembro.</summary>
    public async Task<List<Guid>> OrganizacionIdsDeAsync(Guid userId)
    {
        var consulta = (await _organizaciones.GetQueryableAsync())
            .Where(o => o.Miembros.Any(m => m.UserId == userId))
            .Select(o => o.Id);
        return await _ejecutor.ToListAsync(consulta);
    }
}
