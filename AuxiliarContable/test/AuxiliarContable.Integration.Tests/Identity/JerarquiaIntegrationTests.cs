using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Xunit;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable;

/// <summary>
/// La jerarquía Developer → Owner → Administrador → Usuario contra PostgreSQL real.
///
/// <para>Los tests corren con autorización «siempre permitido» (ver
/// <c>AuxiliarContableTestBaseModule</c>), o sea que aquí NO se prueban los permisos: se prueba
/// lo que va encima de ellos. Por eso es la prueba que importa — con todos los permisos
/// concedidos, lo único que impide al Owner ver al Developer es la jerarquía.</para>
/// </summary>
public class JerarquiaIntegrationTests : ConJerarquiaTestBase
{
    private readonly IIdentityUserAppService _usuariosApp;
    private readonly IIdentityRoleAppService _rolesApp;
    private readonly IPermissionAppService _permisosApp;

    public JerarquiaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _usuariosApp = GetRequiredService<IIdentityUserAppService>();
        _rolesApp = GetRequiredService<IIdentityRoleAppService>();
        _permisosApp = GetRequiredService<IPermissionAppService>();
    }

    [Fact]
    public async Task El_Owner_no_ve_al_Developer_en_el_listado_ni_en_el_total()
    {
        var pagina = await ComoAsync(Owner,
            () => _usuariosApp.GetListAsync(new GetIdentityUsersInput { MaxResultCount = 100 }));

        pagina.Items.Select(u => u.UserName).ShouldBe(["carlos", "laura", "tio"], ignoreOrder: true);
        // El total también: un «4» con tres filas delataría que hay alguien más.
        pagina.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task El_Owner_recibe_404_al_pedir_al_Developer_por_cualquier_camino()
    {
        await ComoAsync(Owner, async () =>
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _usuariosApp.GetAsync(Developer.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _usuariosApp.GetRolesAsync(Developer.Id));
            (await _usuariosApp.FindByIdAsync(Developer.Id)).ShouldBeNull();
            (await _usuariosApp.FindByUsernameAsync("roque")).ShouldBeNull();
            (await _usuariosApp.FindByEmailAsync("roque@auxiliar.test")).ShouldBeNull();

            // Ni buscándolo por texto.
            var busqueda = await _usuariosApp.GetListAsync(new GetIdentityUsersInput { Filter = "roque" });
            busqueda.TotalCount.ShouldBe(0);
        });
    }

    [Fact]
    public async Task El_Owner_no_puede_editar_ni_borrar_al_Developer()
    {
        await ComoAsync(Owner, async () =>
        {
            await Should.ThrowAsync<EntityNotFoundException>(() =>
                _usuariosApp.DeleteAsync(Developer.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() =>
                _usuariosApp.UpdateRolesAsync(Developer.Id, new IdentityUserUpdateRolesDto { RoleNames = [] }));
        });

        (await Usuarios.FindByIdAsync(Developer.Id.ToString())).ShouldNotBeNull();
    }

    [Fact]
    public async Task El_Developer_ve_a_todos()
    {
        var pagina = await ComoAsync(Developer,
            () => _usuariosApp.GetListAsync(new GetIdentityUsersInput { MaxResultCount = 100 }));

        pagina.TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task El_Owner_nombra_Administradores()
    {
        var creado = await ComoAsync(Owner, () => _usuariosApp.CreateAsync(new IdentityUserCreateDto
        {
            UserName = "ana",
            Email = "ana@auxiliar.test",
            Password = "1q2w3E*!",
            IsActive = true,
            RoleNames = [AuxiliarRoles.Administrador],
        }));

        // Con la regla de ABP («solo roles que el operador tiene») esto saldría sin roles:
        // el Owner no es Administrador.
        (await RolesDeAsync(creado.Id)).ShouldBe([AuxiliarRoles.Administrador]);
    }

    [Fact]
    public async Task El_Owner_no_puede_crear_otro_Owner_ni_un_Developer()
    {
        var organizacion = await CrearOrganizacionAsync();
        var creado = await ComoAsync(Owner, () => _usuariosApp.CreateAsync(EnOrganizacion(new IdentityUserCreateDto
        {
            UserName = "intruso",
            Email = "intruso@auxiliar.test",
            Password = "1q2w3E*!",
            IsActive = true,
            RoleNames = [AuxiliarRoles.Owner, AuxiliarRoles.Developer, AuxiliarRoles.Usuario],
        }, organizacion)));

        (await RolesDeAsync(creado.Id)).ShouldBe([AuxiliarRoles.Usuario]);
    }

    [Fact]
    public async Task Un_Administrador_solo_asigna_roles_de_usuario()
    {
        var organizacion = await CrearOrganizacionAsync();
        var creado = await ComoAsync(Administrador, () => _usuariosApp.CreateAsync(EnOrganizacion(new IdentityUserCreateDto
        {
            UserName = "sofia",
            Email = "sofia@auxiliar.test",
            Password = "1q2w3E*!",
            IsActive = true,
            RoleNames = [AuxiliarRoles.Administrador, AuxiliarRoles.Usuario],
        }, organizacion)));

        (await RolesDeAsync(creado.Id)).ShouldBe([AuxiliarRoles.Usuario]);
    }

    [Fact]
    public async Task Un_Administrador_no_ve_ni_toca_a_otro_Administrador_ni_al_Owner()
    {
        var otro = await CrearUsuarioAsync("jorge", AuxiliarRoles.Administrador);

        await ComoAsync(Administrador, async () =>
        {
            var pagina = await _usuariosApp.GetListAsync(new GetIdentityUsersInput { MaxResultCount = 100 });
            pagina.Items.Select(u => u.UserName).ShouldBe(["carlos", "laura"], ignoreOrder: true);

            await Should.ThrowAsync<EntityNotFoundException>(() => _usuariosApp.DeleteAsync(otro.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _usuariosApp.DeleteAsync(Owner.Id));
        });
    }

    [Fact]
    public async Task Editarse_a_uno_mismo_no_sube_de_nivel()
    {
        // Pedirse Owner siendo Administrador: la edición se acepta, pero el rol no entra —
        // solo se asigna lo que está por debajo— y el propio se conserva.
        await ComoAsync(Administrador, () =>
            _usuariosApp.UpdateRolesAsync(Administrador.Id, new IdentityUserUpdateRolesDto
            {
                RoleNames = [AuxiliarRoles.Owner, AuxiliarRoles.Developer],
            }));

        (await RolesDeAsync(Administrador.Id)).ShouldBe([AuxiliarRoles.Administrador]);
    }

    [Fact]
    public async Task El_Developer_puede_editar_sus_propios_datos()
    {
        var yo = await ComoAsync(Developer, () => _usuariosApp.GetAsync(Developer.Id));

        var editado = await ComoAsync(Developer, () => _usuariosApp.UpdateAsync(Developer.Id, new IdentityUserUpdateDto
        {
            UserName = yo.UserName,
            Email = yo.Email,
            Name = "Angel",
            Surname = "Roque",
            IsActive = true,
            LockoutEnabled = yo.LockoutEnabled,
            ConcurrencyStamp = yo.ConcurrencyStamp,
        }));

        editado.Name.ShouldBe("Angel");
    }

    [Fact]
    public async Task El_Owner_solo_ve_los_roles_que_puede_asignar()
    {
        var roles = await ComoAsync(Owner, () => _rolesApp.GetAllListAsync());
        roles.Items.Select(r => r.Name).ShouldBe([AuxiliarRoles.Administrador, AuxiliarRoles.Usuario], ignoreOrder: true);

        var asignables = await ComoAsync(Owner, () => _usuariosApp.GetAssignableRolesAsync());
        asignables.Items.Select(r => r.Name).ShouldBe([AuxiliarRoles.Administrador, AuxiliarRoles.Usuario], ignoreOrder: true);
    }

    [Fact]
    public async Task El_Owner_no_abre_los_permisos_del_Developer_ni_los_suyos()
    {
        await ComoAsync(Owner, async () =>
        {
            await Should.ThrowAsync<EntityNotFoundException>(() =>
                _permisosApp.GetAsync(RolePermissionValueProvider.ProviderName, AuxiliarRoles.Developer));
            await Should.ThrowAsync<EntityNotFoundException>(() =>
                _permisosApp.UpdateAsync(RolePermissionValueProvider.ProviderName, AuxiliarRoles.Developer,
                    new UpdatePermissionsDto { Permissions = [] }));
            await Should.ThrowAsync<EntityNotFoundException>(() =>
                _permisosApp.GetAsync(RolePermissionValueProvider.ProviderName, AuxiliarRoles.Owner));
            await Should.ThrowAsync<EntityNotFoundException>(() =>
                _permisosApp.GetAsync(UserPermissionValueProvider.ProviderName, Developer.Id.ToString()));

            // Los de abajo sí.
            var administrador = await _permisosApp.GetAsync(RolePermissionValueProvider.ProviderName, AuxiliarRoles.Administrador);
            administrador.Groups.ShouldNotBeEmpty();
        });
    }

    [Fact]
    public async Task Los_roles_nacen_estaticos_y_con_sus_permisos()
    {
        var gestor = GetRequiredService<IPermissionManager>();

        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var (nombre, permisos) in PermisosInicialesPorRol.Mapa)
            {
                var rol = await Roles.FindByNameAsync(nombre);
                rol.ShouldNotBeNull();
                rol.IsStatic.ShouldBeTrue();

                foreach (var permiso in permisos)
                {
                    (await gestor.GetAsync(permiso, RolePermissionValueProvider.ProviderName, nombre))
                        .IsGranted.ShouldBeTrue($"{nombre} debería nacer con {permiso}");
                }
            }
        });
    }

    [Fact]
    public async Task Volver_a_sembrar_no_devuelve_un_permiso_que_el_Owner_quito()
    {
        var gestor = GetRequiredService<IPermissionManager>();
        // Uno que ninguna versión de PermisosNuevosPorVersion vuelve a conceder.
        const string permiso = "AbpIdentity.Users.Delete";

        await WithUnitOfWorkAsync(() =>
            gestor.SetAsync(permiso, RolePermissionValueProvider.ProviderName, AuxiliarRoles.Administrador, false));

        // Lo que pasa en cada despliegue: el DbMigrator vuelve a correr los sembradores.
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<AuxiliarRolesDataSeedContributor>().SeedAsync(new DataSeedContext()));

        await WithUnitOfWorkAsync(async () =>
            (await gestor.GetAsync(permiso, RolePermissionValueProvider.ProviderName, AuxiliarRoles.Administrador))
                .IsGranted.ShouldBeFalse());
    }

    [Fact]
    public async Task Los_permisos_iniciales_existen_de_verdad()
    {
        var definiciones = GetRequiredService<IPermissionDefinitionManager>();

        var todos = PermisosInicialesPorRol.Mapa.Values.SelectMany(p => p)
            .Concat(PermisosNuevosPorVersion.Versiones.SelectMany(v => v.Mapa.Values.SelectMany(p => p)));

        foreach (var permiso in todos.Distinct())
        {
            (await definiciones.GetOrNullAsync(permiso)).ShouldNotBeNull($"«{permiso}» no es un permiso definido");
            (await definiciones.GetOrNullAsync(permiso))!.IsEnabled.ShouldBeTrue($"«{permiso}» está apagado");
        }
    }

    /// <summary>Lo que no aplica a una sola organización no sale en la pantalla de permisos.</summary>
    [Theory]
    [InlineData("AbpTenantManagement.Tenants")]
    [InlineData("FeatureManagement.ManageHostFeatures")]
    [InlineData("SettingManagement.Emailing")]
    [InlineData("SettingManagement.TimeZone")]
    public async Task Los_permisos_que_no_aplican_estan_apagados(string permiso)
    {
        var definicion = await GetRequiredService<IPermissionDefinitionManager>().GetOrNullAsync(permiso);
        definicion.ShouldNotBeNull();
        definicion.IsEnabled.ShouldBeFalse();
    }
}
