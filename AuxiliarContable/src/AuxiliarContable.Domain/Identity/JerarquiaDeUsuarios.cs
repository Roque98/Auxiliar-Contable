using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AuxiliarContable.Identity;

/// <summary>
/// Aplica <see cref="AuxiliarRoles"/> sobre datos reales: el nivel de quien llama, el de otra
/// persona y qué roles le quedan fuera de alcance.
///
/// <para>⚠️ Lo que no se alcanza responde <b>404, no 403</b>
/// (<see cref="EntityNotFoundException"/>). Un 403 confirmaría que ese usuario existe, y
/// justo lo que se protege es que el Owner no sepa que hay una cuenta Developer.</para>
/// </summary>
public class JerarquiaDeUsuarios : DomainService
{
    private readonly ICurrentUser _actual;
    private readonly IdentityUserManager _usuarios;
    private readonly IIdentityRoleRepository _roles;
    private readonly IRepository<IdentityUser, Guid> _consultaDeUsuarios;
    private readonly NivelesDeRoles _niveles;

    public JerarquiaDeUsuarios(
        ICurrentUser actual,
        IdentityUserManager usuarios,
        IIdentityRoleRepository roles,
        IRepository<IdentityUser, Guid> consultaDeUsuarios,
        NivelesDeRoles niveles)
    {
        _niveles = niveles;
        _actual = actual;
        _usuarios = usuarios;
        _roles = roles;
        _consultaDeUsuarios = consultaDeUsuarios;
    }

    /// <summary>
    /// El nivel de cada rol (dato del rol; ver <see cref="NivelesDeRoles"/>, que lo tiene en
    /// caché). No se guarda en esta instancia: un servicio puede vivir más que una petición
    /// (en los tests, sin ir más lejos) y vería roles viejos.
    /// </summary>
    public Task<IReadOnlyDictionary<string, int>> NivelesAsync()
    {
        return _niveles.MapaAsync();
    }

    /// <summary>
    /// Sale de los roles de la sesión, igual que el propio ABP decide quién es admin
    /// (<c>CurrentUser.IsInRole</c>). Con <c>UseDynamicClaims</c> activo los roles se
    /// refrescan en cada petición, así que quitarle un rol a alguien surte efecto sin que
    /// tenga que volver a entrar.
    /// </summary>
    public async Task<int> NivelActualAsync()
    {
        return AuxiliarRoles.NivelDeRoles(_actual.Roles, await NivelesAsync());
    }

    public async Task<int> NivelDeAsync(IdentityUser usuario)
    {
        return AuxiliarRoles.NivelDeRoles(await _usuarios.GetRolesAsync(usuario), await NivelesAsync());
    }

    public async Task<int> NivelDeRolAsync(string rol)
    {
        return AuxiliarRoles.Nivel(rol, await NivelesAsync());
    }

    public async Task<bool> PuedeAsignarAsync(string rol)
    {
        return AuxiliarRoles.PuedeAsignar(await NivelActualAsync(), rol, await NivelesAsync());
    }

    /// <summary>Lanza 404 si quien llama no puede ver a <paramref name="usuario"/>.</summary>
    public async Task ExigirVisibleAsync(IdentityUser usuario)
    {
        var objetivo = await NivelDeAsync(usuario);
        if (!AuxiliarRoles.PuedeVer(await NivelActualAsync(), objetivo, usuario.Id == _actual.Id))
        {
            throw new EntityNotFoundException(typeof(IdentityUser), usuario.Id);
        }
    }

    /// <summary>
    /// Lanza 404 si quien llama no puede editar, borrar o cambiar roles/permisos de
    /// <paramref name="usuario"/>.
    ///
    /// <para><paramref name="permitirUnoMismo"/>: editar SUS datos o sus roles no sube a
    /// nadie de nivel —el filtro de roles solo deja asignar lo que está por debajo y conserva
    /// lo que uno no puede quitar—, así que la edición propia se permite. Sin esto el Developer
    /// no podía ni cambiarse la contraseña desde Usuarios (salía «no existe»). Para permisos
    /// individuales sigue sin contar: ahí no aporta nada y no hay por qué abrirlo.</para>
    /// </summary>
    public async Task ExigirGestionableAsync(IdentityUser usuario, bool permitirUnoMismo = false)
    {
        if (permitirUnoMismo && usuario.Id == _actual.Id)
        {
            return;
        }

        var objetivo = await NivelDeAsync(usuario);
        if (!AuxiliarRoles.PuedeGestionar(await NivelActualAsync(), objetivo) || usuario.Id == _actual.Id)
        {
            throw new EntityNotFoundException(typeof(IdentityUser), usuario.Id);
        }
    }

    public async Task<bool> PuedeVerAsync(IdentityUser usuario)
    {
        var objetivo = await NivelDeAsync(usuario);
        return AuxiliarRoles.PuedeVer(await NivelActualAsync(), objetivo, usuario.Id == _actual.Id);
    }

    /// <summary>
    /// Los roles que quien llama NO alcanza: los de su nivel o superior. Quien tenga alguno
    /// queda fuera de sus listados (salvo uno mismo). Vacío para el Developer.
    /// </summary>
    public async Task<List<IdentityRole>> RolesFueraDeAlcanceAsync()
    {
        var nivel = await NivelActualAsync();
        var niveles = await NivelesAsync();
        var todos = await _roles.GetListAsync();
        return todos.Where(r => !AuxiliarRoles.PuedeAsignar(nivel, r.Name, niveles)).ToList();
    }

    /// <summary>
    /// Las cuentas Developer, salvo si quien llama es Developer (entonces, ninguna).
    ///
    /// <para>Para los DATOS DE TRABAJO que no son de personas (documentos, facturas): ahí el
    /// equipo lo ve todo y el cliente ve lo de sus organizaciones, lo haya subido quien lo haya
    /// subido. Lo único que sigue escondido es la actividad del Developer (sus pruebas), que
    /// delataría la cuenta. Para listar PERSONAS se sigue usando
    /// <see cref="UsuariosFueraDeAlcanceAsync"/>.</para>
    /// </summary>
    public async Task<List<Guid>> DevelopersOcultosAsync()
    {
        if (await NivelActualAsync() >= AuxiliarRoles.NivelDeveloper)
        {
            return [];
        }

        return await DevelopersAsync();
    }

    /// <summary>
    /// Las cuentas Developer, sin mirar quién pregunta. Solo para decidir cosas por dentro (p. ej.
    /// «esta prueba es del Developer, nadie más la ve»): nunca para enseñarlas.
    /// </summary>
    public async Task<List<Guid>> DevelopersAsync()
    {
        var rol = await _roles.FindByNormalizedNameAsync(AuxiliarRoles.Developer.ToUpperInvariant());
        if (rol == null)
        {
            return [];
        }

        var consulta = (await _consultaDeUsuarios.GetQueryableAsync())
            .Where(u => u.Roles.Any(r => r.RoleId == rol.Id))
            .Select(u => u.Id);

        return await AsyncExecuter.ToListAsync(consulta);
    }

    /// <summary>
    /// Los ids de las personas que quien llama NO puede ver (nunca él mismo). Vacío para el
    /// Developer.
    ///
    /// <para>⚠️ Todo lo que enseñe datos DE OTROS usuarios —no solo la lista de usuarios—
    /// tiene que filtrar por esto. Un análisis del Developer en «ver todos» delataría con su
    /// autor la cuenta que la jerarquía esconde.</para>
    /// </summary>
    public async Task<List<Guid>> UsuariosFueraDeAlcanceAsync()
    {
        var ocultos = (await RolesFueraDeAlcanceAsync()).Select(r => r.Id).ToList();
        if (ocultos.Count == 0)
        {
            return [];
        }

        var yo = _actual.Id;
        var consulta = (await _consultaDeUsuarios.GetQueryableAsync())
            .Where(u => u.Id != yo && u.Roles.Any(r => ocultos.Contains(r.RoleId)))
            .Select(u => u.Id);

        return await AsyncExecuter.ToListAsync(consulta);
    }

    public async Task<List<IdentityRole>> RolesAlAlcanceAsync()
    {
        var nivel = await NivelActualAsync();
        var niveles = await NivelesAsync();
        var todos = await _roles.GetListAsync();
        return todos.Where(r => AuxiliarRoles.PuedeAsignar(nivel, r.Name, niveles)).OrderBy(r => r.Name).ToList();
    }

    public Guid? IdActual => _actual.Id;
}
