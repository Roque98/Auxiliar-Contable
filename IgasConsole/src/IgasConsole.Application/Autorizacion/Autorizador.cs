using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Identity;
using IgasConsole.Organizaciones;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Users;

namespace IgasConsole.Autorizacion;

/// <summary>
/// Quién mira, decidido UNA vez por petición: las tres piezas de la autorización juntas.
///
/// <list type="bullet">
/// <item><b>Nivel</b> (del rol global): <see cref="Developer"/>, y a quién alcanza en la jerarquía.</item>
/// <item><b>Permisos</b>: <see cref="Equipo"/> = tiene <c>Organizaciones</c> (Owner,
/// Administrador, Developer): no lo limita ninguna organización.</item>
/// <item><b>Organización</b> (alcance): de cuáles es miembro y con qué papel
/// (<see cref="RolEnOrganizacion"/>), para quien no es del equipo.</item>
/// </list>
/// </summary>
public sealed record QuienMira(
    Guid Yo,
    bool Equipo,
    bool Developer,
    IReadOnlyDictionary<Guid, RolEnOrganizacion> Organizaciones)
{
    /// <summary>¿Puede hacer esto en esa organización? El equipo, en todas.</summary>
    public bool Puede(Guid organizacionId, AccionEnOrganizacion accion) =>
        Equipo || (Organizaciones.TryGetValue(organizacionId, out var rol) && PermisosEnOrganizacion.Permite(rol, accion));

    /// <summary>Las organizaciones (de las suyas) donde puede hacer esto. Para el equipo no aplica: ve todas.</summary>
    public List<Guid> OrganizacionesDonde(AccionEnOrganizacion accion) =>
        Organizaciones.Where(o => PermisosEnOrganizacion.Permite(o.Value, accion)).Select(o => o.Key).ToList();
}

/// <summary>
/// El ÚNICO punto que decide quién puede qué en las organizaciones (los clientes) y lo que cuelga de ellas (lo que en
/// Cedar u OpenFGA es el «PDP»). Los servicios preguntan aquí en vez de decidir cada uno a su
/// manera: antes la regla estaba repartida en cuatro sitios, y añadir una condición en uno y
/// olvidarla en otro no daba ningún error.
///
/// <para>Si un día hace falta un motor externo (OpenFGA, para compartir documentos sueltos o
/// sub-organizaciones), el cambio queda dentro de esta clase.</para>
///
/// <para>Las reglas de PERSONAS (quién ve y gestiona a quién por nivel) siguen en
/// <see cref="JerarquiaDeUsuarios"/>, que esta clase usa.</para>
/// </summary>
public class Autorizador : ITransientDependency
{
    private readonly ICurrentUser _actual;
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly AlcanceDeOrganizaciones _alcance;

    public Autorizador(ICurrentUser actual, JerarquiaDeUsuarios jerarquia, AlcanceDeOrganizaciones alcance)
    {
        _actual = actual;
        _jerarquia = jerarquia;
        _alcance = alcance;
    }

    /// <summary>
    /// Se calcula en cada llamada (una por operación, en la práctica): guardarlo en la
    /// instancia hacía que un servicio que vive más que una petición viera membresías viejas.
    /// </summary>
    public async Task<QuienMira> QuienMiraAsync()
    {
        var yo = _actual.GetId();
        var developer = await _jerarquia.NivelActualAsync() >= IgasRoles.NivelDeveloper;
        var equipo = developer || await _alcance.VeTodasAsync();
        return new QuienMira(yo, equipo, developer, await _alcance.MembresiasDeAsync(yo));
    }

    /// <summary>404 si no puede hacer esto en esa organización (no se confirma que exista).</summary>
    public async Task ExigirAsync(Guid organizacionId, AccionEnOrganizacion accion)
    {
        if (!(await QuienMiraAsync()).Puede(organizacionId, accion))
        {
            throw new EntityNotFoundException(typeof(Organizacion), organizacionId);
        }
    }
}
