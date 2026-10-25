using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using IgasConsole.Identity;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Data;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using IdentityRole = Volo.Abp.Identity.IdentityRole;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace IgasConsole;

/// <summary>
/// Base para los tests que necesitan la jerarquía montada: siembra los roles y crea una
/// persona por nivel (roque = Developer, tio = Owner, laura = Administrador, carlos =
/// Usuario). <see cref="ComoAsync{T}"/> ejecuta algo con la sesión de una de ellas.
///
/// <para>Recuerda que la autorización está en «siempre permitido»: aquí se prueba la
/// jerarquía, no los permisos.</para>
/// </summary>
public abstract class ConJerarquiaTestBase : IntegrationTestBase
{
    protected const string Clave = "1q2w3E*!";

    protected readonly IdentityUserManager Usuarios;
    protected readonly IdentityRoleManager Roles;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly IGuidGenerator _guids;

    protected IdentityUser Developer = null!;
    protected IdentityUser Owner = null!;
    protected IdentityUser Administrador = null!;
    protected IdentityUser Usuario = null!;

    protected ConJerarquiaTestBase(PostgresFixture fixture) : base(fixture)
    {
        Usuarios = GetRequiredService<IdentityUserManager>();
        Roles = GetRequiredService<IdentityRoleManager>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            // El rol admin lo crea ABP al sembrar su usuario; aquí se crea a mano para no
            // arrastrar el resto de sembradores (OpenIddict, ajustes) a cada test.
            (await Roles.CreateAsync(new IdentityRole(_guids.Create(), IgasRoles.Developer) { IsStatic = true }))
                .CheckErrors();

            await GetRequiredService<IgasRolesDataSeedContributor>().SeedAsync(new DataSeedContext());
        });

        Developer = await CrearUsuarioAsync("roque", IgasRoles.Developer);
        Owner = await CrearUsuarioAsync("tio", IgasRoles.Owner);
        Administrador = await CrearUsuarioAsync("laura", IgasRoles.Administrador);
        Usuario = await CrearUsuarioAsync("carlos", IgasRoles.Usuario);
    }

    protected async Task<IdentityUser> CrearUsuarioAsync(string nombre, string rol)
    {
        IdentityUser usuario = null!;

        await WithUnitOfWorkAsync(async () =>
        {
            usuario = new IdentityUser(_guids.Create(), nombre, $"{nombre}@igas.test");
            (await Usuarios.CreateAsync(usuario, Clave)).CheckErrors();
            (await Usuarios.AddToRoleAsync(usuario, rol)).CheckErrors();
        });

        return usuario;
    }

    /// <summary>
    /// Una organización para dar de alta clientes: desde que el alta la exige, un Usuario
    /// no se puede crear sin ella. No se crea sola en la base de los tests para no alterar
    /// los que cuentan organizaciones.
    /// </summary>
    protected async Task<Guid> CrearOrganizacionAsync(string nombre = "Cliente de prueba")
    {
        var id = _guids.Create();
        var organizacion = new IgasConsole.Organizaciones.Organizacion(id, nombre, null);
        await WithUnitOfWorkAsync(() =>
            GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<IgasConsole.Organizaciones.Organizacion, Guid>>()
                .InsertAsync(organizacion, autoSave: true));
        return id;
    }

    /// <summary>Pone la organización en el DTO de alta o edición de usuarios de ABP.</summary>
    protected static T EnOrganizacion<T>(T dto, Guid organizacionId) where T : Volo.Abp.ObjectExtending.ExtensibleObject
    {
        dto.ExtraProperties[IgasIdentityUserAppService.PropiedadOrganizacion] = organizacionId.ToString();
        return dto;
    }

    protected async Task<List<string>> RolesDeAsync(Guid id)
    {
        List<string> roles = [];

        await WithUnitOfWorkAsync(async () =>
        {
            var usuario = await Usuarios.GetByIdAsync(id);
            roles = (await Usuarios.GetRolesAsync(usuario)).ToList();
        });

        return roles;
    }

    /// <summary>El rol de la sesión sale del usuario: el primero que tenga.</summary>
    protected Task ComoAsync(IdentityUser usuario, Func<Task> accion)
    {
        return ComoAsync(usuario, async () =>
        {
            await accion();
            return true;
        });
    }

    protected async Task<T> ComoAsync<T>(IdentityUser usuario, Func<Task<T>> accion)
    {
        var roles = await RolesDeAsync(usuario.Id);

        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, usuario.Id.ToString()),
            new(AbpClaimTypes.UserName, usuario.UserName),
        };
        claims.AddRange(roles.Select(r => new Claim(AbpClaimTypes.Role, r)));

        using (_principal.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))))
        {
            T resultado = default!;
            await WithUnitOfWorkAsync(async () => resultado = await accion());
            return resultado;
        }
    }
}
