using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using AuxiliarContable.Invitaciones;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable;

/// <summary>
/// Etapa 1 de la autorización: el NIVEL es un dato del rol (no su nombre) y cada persona tiene
/// un PAPEL dentro de cada organización (Administrador, Miembro, Lector), que decide lo que
/// puede hacer ahí. Un administrador de organización gestiona a su gente y a nadie más.
/// </summary>
public class PapelesYNivelesIntegrationTests : ConJerarquiaTestBase
{
    private readonly IIdentityRoleAppService _roles;
    private readonly IIdentityUserAppService _usuarios;
    private readonly IOrganizacionAppService _organizaciones;
    private readonly IMiOrganizacionAppService _miOrganizacion;

    public PapelesYNivelesIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _roles = GetRequiredService<IIdentityRoleAppService>();
        _usuarios = GetRequiredService<IIdentityUserAppService>();
        _organizaciones = GetRequiredService<IOrganizacionAppService>();
        _miOrganizacion = GetRequiredService<IMiOrganizacionAppService>();
    }

    private Task<IdentityRoleDto> CrearRolAsync(IdentityUser como, string nombre, NivelDeRol nivel)
    {
        var dto = new IdentityRoleCreateDto { Name = nombre };
        dto.SetProperty(AuxiliarRoles.PropiedadNivel, nivel);
        return ComoAsync(como, () => _roles.CreateAsync(dto));
    }

    /// <summary>Norte, con carlos (cliente) de administrador.</summary>
    private async Task<Guid> NorteConCarlosDeAdministradorAsync()
    {
        var norte = await ComoAsync(Owner, () => _organizaciones.CreateAsync(new GuardarOrganizacionDto { Nombre = "Norte" }));
        await ComoAsync(Owner, () => _organizaciones.AgregarMiembroAsync(norte.Id,
            new AgregarMiembroDto { UserId = Usuario.Id, Rol = RolEnOrganizacion.Administrador }));
        return norte.Id;
    }

    private async Task<IdentityUser> ClienteEnAsync(Guid organizacion, string nombre, RolEnOrganizacion rol)
    {
        var u = await CrearUsuarioAsync(nombre, AuxiliarRoles.Usuario);
        await ComoAsync(Owner, () => _organizaciones.AgregarMiembroAsync(organizacion, new AgregarMiembroDto { UserId = u.Id, Rol = rol }));
        return u;
    }

    // --- El nivel es un dato del rol ------------------------------------------------

    [Fact]
    public async Task Un_rol_nuevo_de_nivel_Administrador_gestiona_a_los_usuarios_y_a_nadie_mas()
    {
        var rol = await CrearRolAsync(Owner, "supervisor", NivelDeRol.Administrador);
        rol.GetProperty<NivelDeRol>(AuxiliarRoles.PropiedadNivel).ShouldBe(NivelDeRol.Administrador);
        var sara = await CrearUsuarioAsync("sara", "supervisor");

        (await ComoAsync(sara, () => _usuarios.GetAsync(Usuario.Id))).UserName.ShouldBe("carlos");
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(sara, () => _usuarios.GetAsync(Administrador.Id)));
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(sara, () => _usuarios.GetAsync(Owner.Id)));
    }

    [Fact]
    public async Task Nadie_crea_un_rol_de_su_nivel_o_superior()
    {
        await Should.ThrowAsync<UserFriendlyException>(() => CrearRolAsync(Owner, "socio", NivelDeRol.Owner));
        (await CrearRolAsync(Developer, "socio", NivelDeRol.Owner)).Name.ShouldBe("socio");
        // El nivel Developer es solo del rol admin, lo pida quien lo pida.
        await Should.ThrowAsync<UserFriendlyException>(() => CrearRolAsync(Developer, "otro-dev", NivelDeRol.Developer));
    }

    [Fact]
    public async Task Los_roles_propios_no_cambian_de_nivel()
    {
        var administrador = (await ComoAsync(Owner, () => _roles.GetAllListAsync())).Items.Single(r => r.Name == AuxiliarRoles.Administrador);
        var dto = new IdentityRoleUpdateDto { Name = administrador.Name, ConcurrencyStamp = administrador.ConcurrencyStamp };
        dto.SetProperty(AuxiliarRoles.PropiedadNivel, NivelDeRol.Usuario);

        var editado = await ComoAsync(Owner, () => _roles.UpdateAsync(administrador.Id, dto));

        editado.GetProperty<NivelDeRol>(AuxiliarRoles.PropiedadNivel).ShouldBe(NivelDeRol.Administrador);
        // Y laura sigue pudiendo gestionar a carlos.
        (await ComoAsync(Administrador, () => _usuarios.GetAsync(Usuario.Id))).UserName.ShouldBe("carlos");
    }

    // --- Papeles dentro de la organización --------------------------------------------

    [Fact]
    public async Task Un_lector_ve_su_organizacion_pero_no_sube_ni_gestiona()
    {
        var norte = await NorteConCarlosDeAdministradorAsync();
        var maria = await ClienteEnAsync(norte, "maria", RolEnOrganizacion.Lector);

        var deMaria = (await ComoAsync(maria, () => _miOrganizacion.GetListAsync())).ShouldHaveSingleItem();
        deMaria.MiRol.ShouldBe(RolEnOrganizacion.Lector);
        deMaria.PuedoGestionarPersonas.ShouldBeFalse();
        PermisosEnOrganizacion.Permite(RolEnOrganizacion.Lector, AccionEnOrganizacion.Ver).ShouldBeTrue();
        PermisosEnOrganizacion.Permite(RolEnOrganizacion.Lector, AccionEnOrganizacion.Subir).ShouldBeFalse();
        PermisosEnOrganizacion.Permite(RolEnOrganizacion.Miembro, AccionEnOrganizacion.Subir).ShouldBeTrue();
    }

    [Fact]
    public async Task El_administrador_de_la_organizacion_gestiona_a_su_gente()
    {
        var norte = await NorteConCarlosDeAdministradorAsync();
        var maria = await ClienteEnAsync(norte, "maria", RolEnOrganizacion.Lector);

        var mia = (await ComoAsync(Usuario, () => _miOrganizacion.GetListAsync())).ShouldHaveSingleItem();
        mia.MiRol.ShouldBe(RolEnOrganizacion.Administrador);
        mia.PuedoGestionarPersonas.ShouldBeTrue();
        mia.Miembros.Select(m => m.UserName).ShouldBe(["carlos", "maria"]);

        (await ComoAsync(Usuario, () => _miOrganizacion.CambiarRolAsync(norte, maria.Id,
            new CambiarRolEnOrganizacionDto { Rol = RolEnOrganizacion.Miembro })))
            .Miembros.Single(m => m.UserName == "maria").Rol.ShouldBe(RolEnOrganizacion.Miembro);

        (await ComoAsync(Usuario, () => _miOrganizacion.QuitarAsync(norte, maria.Id)))
            .Miembros.Select(m => m.UserName).ShouldBe(["carlos"]);

        // María ya no administra nada (ni lo hacía): para ella no hay a quién gestionar.
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(maria, () => _miOrganizacion.QuitarAsync(norte, Usuario.Id)));
    }

    [Fact]
    public async Task El_ultimo_administrador_no_deja_de_serlo()
    {
        var norte = await NorteConCarlosDeAdministradorAsync();

        var error = await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario, () =>
            _miOrganizacion.CambiarRolAsync(norte, Usuario.Id, new CambiarRolEnOrganizacionDto { Rol = RolEnOrganizacion.Miembro })));
        error.Code.ShouldBe("AuxiliarContable:UltimoAdministradorDeOrganizacion");
    }

    [Fact]
    public async Task Un_miembro_no_gestiona_y_nadie_toca_otra_organizacion_ni_al_equipo()
    {
        var norte = await NorteConCarlosDeAdministradorAsync();
        var maria = await ClienteEnAsync(norte, "maria", RolEnOrganizacion.Miembro);
        var sur = (await ComoAsync(Owner, () => _organizaciones.CreateAsync(new GuardarOrganizacionDto { Nombre = "Sur" }))).Id;
        await ComoAsync(Owner, () => _organizaciones.AgregarMiembroAsync(norte, new AgregarMiembroDto { UserId = Administrador.Id }));

        // Un miembro: ve su organización, pero sin la gente.
        var deMaria = (await ComoAsync(maria, () => _miOrganizacion.GetListAsync())).ShouldHaveSingleItem();
        deMaria.PuedoGestionarPersonas.ShouldBeFalse();
        deMaria.Miembros.ShouldBeEmpty();
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(maria, () =>
            _miOrganizacion.InvitarAsync(norte, new InvitarAMiOrganizacionDto { Email = "x@auxiliar.test" })));

        // El administrador de Norte: otra organización no existe para él.
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () =>
            _miOrganizacion.InvitarAsync(sur, new InvitarAMiOrganizacionDto { Email = "x@auxiliar.test" })));

        // Alguien del equipo dentro de su organización tampoco: ni sale ni se toca.
        var mia = (await ComoAsync(Usuario, () => _miOrganizacion.GetListAsync())).Single();
        mia.Miembros.Select(m => m.UserName).ShouldNotContain("laura");
        await Should.ThrowAsync<EntityNotFoundException>(() => ComoAsync(Usuario, () =>
            _miOrganizacion.QuitarAsync(norte, Administrador.Id)));
    }

    [Fact]
    public async Task El_administrador_invita_y_quien_acepta_entra_con_su_papel()
    {
        var norte = await NorteConCarlosDeAdministradorAsync();

        var mia = await ComoAsync(Usuario, () => _miOrganizacion.InvitarAsync(norte,
            new InvitarAMiOrganizacionDto { Email = "olga@auxiliar.test", Rol = RolEnOrganizacion.Lector }));
        var pendiente = mia.Pendientes.ShouldHaveSingleItem();
        pendiente.Email.ShouldBe("olga@auxiliar.test");

        var token = string.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            var invitacion = await GetRequiredService<IRepository<Invitacion, Guid>>().GetAsync(pendiente.Id);
            invitacion.Rol.ShouldBe(AuxiliarRoles.Usuario);
            token = await GetRequiredService<GestorDeInvitaciones>().EmitirTokenAsync(invitacion);
        });
        await WithUnitOfWorkAsync(() => GetRequiredService<IRegistroAppService>().AceptarAsync(new AceptarInvitacionDto
        {
            Token = token,
            UserName = "olga",
            Password = Clave,
        }));

        (await ComoAsync(Usuario, () => _miOrganizacion.GetListAsync())).Single()
            .Miembros.Single(m => m.UserName == "olga").Rol.ShouldBe(RolEnOrganizacion.Lector);
    }
}
