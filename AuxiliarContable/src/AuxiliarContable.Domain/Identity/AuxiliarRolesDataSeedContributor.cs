using Volo.Abp.SettingManagement;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace AuxiliarContable.Identity;

/// <summary>
/// Crea los roles Owner, Administrador y Usuario con sus permisos de partida.
///
/// <para>⚠️ Los permisos se siembran <b>solo cuando el rol se crea</b>, nunca después. El
/// DbMigrator corre en cada despliegue y <see cref="IPermissionDataSeeder"/> solo AÑADE lo
/// que falta: sembrar en cada pasada devolvería, deploy tras deploy, cualquier permiso que el
/// Owner le hubiera quitado a un rol desde la pantalla. Sin error y sin rastro.</para>
///
/// <para>Los tres nacen <c>IsStatic</c>: ABP no deja renombrar ni borrar un rol estático
/// (<c>IdentityRoleManager</c>), y la jerarquía se apoya en sus nombres.</para>
/// </summary>
public class AuxiliarRolesDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IIdentityRoleRepository _roles;
    private readonly IdentityRoleManager _gestorDeRoles;
    private readonly ILookupNormalizer _normalizador;
    private readonly IPermissionDataSeeder _permisos;
    private readonly IGuidGenerator _guids;
    private readonly ISettingManager _ajustes;

    public AuxiliarRolesDataSeedContributor(
        IIdentityRoleRepository roles,
        IdentityRoleManager gestorDeRoles,
        ILookupNormalizer normalizador,
        IPermissionDataSeeder permisos,
        IGuidGenerator guids,
        ISettingManager ajustes)
    {
        _roles = roles;
        _gestorDeRoles = gestorDeRoles;
        _normalizador = normalizador;
        _permisos = permisos;
        _guids = guids;
        _ajustes = ajustes;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        foreach (var (nombre, permisos) in PermisosInicialesPorRol.Mapa)
        {
            var existente = await _roles.FindByNormalizedNameAsync(_normalizador.NormalizeName(nombre));
            if (existente != null)
            {
                continue;
            }

            var rol = new IdentityRole(_guids.Create(), nombre)
            {
                IsStatic = true,
                IsPublic = true,
                // Marcado por defecto en el alta de usuarios: es el caso común.
                IsDefault = nombre == AuxiliarRoles.Usuario,
            };

            (await _gestorDeRoles.CreateAsync(rol)).CheckErrors();

            await _permisos.SeedAsync(RolePermissionValueProvider.ProviderName, nombre, permisos);
        }

        await ConcederPermisosNuevosAsync();
        await PonerNivelesAsync();
    }

    /// <summary>
    /// El nivel de los roles propios como DATO del rol (<see cref="AuxiliarRoles.PropiedadNivel"/>),
    /// solo si aún no lo tienen: la jerarquía ya no lo deduce del nombre, y así la pantalla de
    /// Roles lo enseña. Los propios no cambian de nivel (lo impide el servicio de roles).
    /// </summary>
    private async Task PonerNivelesAsync()
    {
        foreach (var nombre in AuxiliarRoles.Propios)
        {
            var rol = await _roles.FindByNormalizedNameAsync(_normalizador.NormalizeName(nombre));
            if (rol == null || rol.GetProperty(AuxiliarRoles.PropiedadNivel) != null)
            {
                continue;
            }

            // Sin validar: el del Developer (3) no es un valor de NivelDeRol a propósito.
            rol.SetProperty(AuxiliarRoles.PropiedadNivel, AuxiliarRoles.Nivel(nombre), validate: false);
            (await _gestorDeRoles.UpdateAsync(rol)).CheckErrors();
        }
    }

    /// <summary>
    /// Las versiones de <see cref="PermisosNuevosPorVersion"/> que aún no se aplicaron, a los
    /// roles que existen. Se apuntan en un ajuste global para no repetirlas: repetir en cada
    /// despliegue devolvería permisos que el Owner quitó a propósito.
    /// </summary>
    private async Task ConcederPermisosNuevosAsync()
    {
        var aplicada = int.TryParse(
            await _ajustes.GetOrNullGlobalAsync(Settings.AuxiliarContableSettings.VersionPermisosSembrados), out var v) ? v : 0;

        var pendientes = PermisosNuevosPorVersion.Versiones.Where(x => x.Version > aplicada).OrderBy(x => x.Version).ToList();
        if (pendientes.Count == 0)
        {
            return;
        }

        foreach (var (_, mapa) in pendientes)
        {
            foreach (var (nombre, permisos) in mapa)
            {
                if (await _roles.FindByNormalizedNameAsync(_normalizador.NormalizeName(nombre)) != null)
                {
                    await _permisos.SeedAsync(RolePermissionValueProvider.ProviderName, nombre, permisos);
                }
            }
        }

        await _ajustes.SetGlobalAsync(
            Settings.AuxiliarContableSettings.VersionPermisosSembrados,
            pendientes.Max(x => x.Version).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
