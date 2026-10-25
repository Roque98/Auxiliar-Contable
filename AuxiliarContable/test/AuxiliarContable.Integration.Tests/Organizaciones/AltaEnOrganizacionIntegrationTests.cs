using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using AuxiliarContable.Invitaciones;
using AuxiliarContable.Organizaciones;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// La regla de alta: un cliente (nivel Usuario) entra DENTRO de una organización, sea a mano
/// (Usuarios) o por invitación. Al equipo no se le pide ni se le mete en ninguna.
/// </summary>
public class AltaEnOrganizacionIntegrationTests : ConJerarquiaTestBase
{
    private readonly IIdentityUserAppService _usuariosApp;
    private readonly IOrganizacionAppService _organizaciones;
    private readonly IInvitacionAppService _invitaciones;
    private readonly IRegistroAppService _registro;
    private readonly IMembresiaAppService _membresias;

    public AltaEnOrganizacionIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _usuariosApp = GetRequiredService<IIdentityUserAppService>();
        _organizaciones = GetRequiredService<IOrganizacionAppService>();
        _invitaciones = GetRequiredService<IInvitacionAppService>();
        _registro = GetRequiredService<IRegistroAppService>();
        _membresias = GetRequiredService<IMembresiaAppService>();
    }

    private static IdentityUserCreateDto Alta(string usuario, string rol) => new()
    {
        UserName = usuario,
        Email = usuario + "@auxiliar.test",
        Password = Clave,
        IsActive = true,
        RoleNames = [rol],
    };

    private async Task<bool> ExisteAsync(string usuario)
    {
        var existe = false;
        await WithUnitOfWorkAsync(async () => existe = await Usuarios.FindByNameAsync(usuario) != null);
        return existe;
    }

    private async Task<string[]> MiembrosAsync(Guid organizacion)
    {
        var detalle = await ComoAsync(Developer, () => _organizaciones.GetAsync(organizacion));
        return detalle.ListaDeMiembros.Select(m => m.UserName).OrderBy(x => x).ToArray();
    }

    [Fact]
    public async Task Un_cliente_no_se_da_de_alta_sin_organizacion()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            ComoAsync(Administrador, () => _usuariosApp.CreateAsync(Alta("luis", AuxiliarRoles.Usuario))));

        ex.Code.ShouldBe("AuxiliarContable:OrganizacionObligatoria");
        // La cuenta tampoco queda: todo iba en la misma unidad de trabajo.
        (await ExisteAsync("luis")).ShouldBeFalse();
    }

    [Fact]
    public async Task El_alta_de_un_cliente_lo_deja_dentro_de_su_organizacion()
    {
        var organizacion = await CrearOrganizacionAsync("Gasolinera Norte");

        var creado = await ComoAsync(Administrador, () => _usuariosApp.CreateAsync(EnOrganizacion(Alta("luis", AuxiliarRoles.Usuario), organizacion)));

        (await MiembrosAsync(organizacion)).ShouldBe(["luis"]);
        var membresias = await ComoAsync(Administrador, () => _membresias.GetDeUsuariosAsync([creado.Id]));
        membresias.ShouldHaveSingleItem().Organizaciones.ShouldHaveSingleItem().Nombre.ShouldBe("Gasolinera Norte");
    }

    [Fact]
    public async Task Al_equipo_no_se_le_pide_ni_se_le_mete_en_una_organizacion()
    {
        var organizacion = await CrearOrganizacionAsync();

        await ComoAsync(Owner, () => _usuariosApp.CreateAsync(Alta("marta", AuxiliarRoles.Administrador)));
        await ComoAsync(Owner, () => _usuariosApp.CreateAsync(EnOrganizacion(Alta("nico", AuxiliarRoles.Administrador), organizacion)));

        (await ExisteAsync("marta")).ShouldBeTrue();
        (await MiembrosAsync(organizacion)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_organizacion_que_no_existe_se_rechaza()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            ComoAsync(Administrador, () => _usuariosApp.CreateAsync(EnOrganizacion(Alta("luis", AuxiliarRoles.Usuario), Guid.NewGuid()))));

        ex.Code.ShouldBe("AuxiliarContable:OrganizacionNoExiste");
        (await ExisteAsync("luis")).ShouldBeFalse();
    }

    [Fact]
    public async Task Editar_a_un_cliente_sin_organizacion_pide_una_y_la_anade()
    {
        // «carlos» nace sin organización (la base de los tests lo crea directo con Identity):
        // es el caso de las cuentas de antes de esta regla.
        var carlos = await ComoAsync(Administrador, () => _usuariosApp.GetAsync(Usuario.Id));
        IdentityUserUpdateDto Edicion() => new()
        {
            UserName = carlos.UserName,
            Email = carlos.Email,
            Name = "Carlos",
            IsActive = true,
            RoleNames = [AuxiliarRoles.Usuario],
            ConcurrencyStamp = carlos.ConcurrencyStamp,
        };

        (await Should.ThrowAsync<BusinessException>(() =>
            ComoAsync(Administrador, () => _usuariosApp.UpdateAsync(Usuario.Id, Edicion())))).Code.ShouldBe("AuxiliarContable:OrganizacionObligatoria");

        var organizacion = await CrearOrganizacionAsync();
        await ComoAsync(Administrador, () => _usuariosApp.UpdateAsync(Usuario.Id, EnOrganizacion(Edicion(), organizacion)));

        (await MiembrosAsync(organizacion)).ShouldBe(["carlos"]);

        // Ya pertenece a una: editarlo sin decir organización no le pide otra.
        carlos = await ComoAsync(Administrador, () => _usuariosApp.GetAsync(Usuario.Id));
        await ComoAsync(Administrador, () => _usuariosApp.UpdateAsync(Usuario.Id, Edicion()));
    }

    [Fact]
    public async Task Invitar_a_un_cliente_exige_organizacion_y_al_aceptar_queda_dentro()
    {
        (await Should.ThrowAsync<BusinessException>(() => ComoAsync(Administrador, () =>
            _invitaciones.CreateAsync(new CrearInvitacionDto { Email = "olga@auxiliar.test", Rol = AuxiliarRoles.Usuario }))))
            .Code.ShouldBe("AuxiliarContable:OrganizacionObligatoria");

        var organizacion = await CrearOrganizacionAsync("Grupo Sur");
        var invitacion = await ComoAsync(Administrador, () =>
            _invitaciones.CreateAsync(new CrearInvitacionDto { Email = "olga@auxiliar.test", Rol = AuxiliarRoles.Usuario, OrganizacionId = organizacion }));
        invitacion.Organizacion.ShouldBe("Grupo Sur");

        var token = await EmitirAsync(invitacion.Id);
        (await _registro.ConsultarAsync(new TokenDeInvitacionDto { Token = token })).Organizacion.ShouldBe("Grupo Sur");

        await WithUnitOfWorkAsync(() => _registro.AceptarAsync(new AceptarInvitacionDto { Token = token, UserName = "olga", Password = Clave }));

        (await MiembrosAsync(organizacion)).ShouldBe(["olga"]);
    }

    [Fact]
    public async Task Si_la_organizacion_se_borro_la_invitacion_no_crea_la_cuenta()
    {
        var organizacion = await CrearOrganizacionAsync();
        var invitacion = await ComoAsync(Administrador, () =>
            _invitaciones.CreateAsync(new CrearInvitacionDto { Email = "pia@auxiliar.test", Rol = AuxiliarRoles.Usuario, OrganizacionId = organizacion }));
        var token = await EmitirAsync(invitacion.Id);
        await ComoAsync(Owner, () => _organizaciones.DeleteAsync(organizacion));

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            WithUnitOfWorkAsync(() => _registro.AceptarAsync(new AceptarInvitacionDto { Token = token, UserName = "pia", Password = Clave })));

        ex.Code.ShouldBe("AuxiliarContable:InvitacionSinOrganizacion");
        (await ExisteAsync("pia")).ShouldBeFalse();
    }

    [Fact]
    public async Task Invitar_al_equipo_no_guarda_organizacion()
    {
        var organizacion = await CrearOrganizacionAsync();

        var invitacion = await ComoAsync(Owner, () =>
            _invitaciones.CreateAsync(new CrearInvitacionDto { Email = "raul@auxiliar.test", Rol = AuxiliarRoles.Administrador, OrganizacionId = organizacion }));

        invitacion.OrganizacionId.ShouldBeNull();
    }

    [Fact]
    public async Task Las_opciones_listan_las_organizaciones_y_las_membresias_no_delatan_al_Developer()
    {
        var organizacion = await CrearOrganizacionAsync("Norte");
        await ComoAsync(Developer, () => _organizaciones.AgregarMiembroAsync(organizacion, new AgregarMiembroDto { UserId = Developer.Id }));

        var opciones = await ComoAsync(Administrador, () => _membresias.GetOpcionesAsync());
        opciones.Organizaciones.Select(o => o.Nombre).ShouldBe(["Norte"]);
        opciones.NivelSinOrganizacion.ShouldBe(AuxiliarRoles.NivelAdministrador);

        (await ComoAsync(Owner, () => _membresias.GetDeUsuariosAsync([Developer.Id]))).ShouldBeEmpty();
        (await ComoAsync(Developer, () => _membresias.GetDeUsuariosAsync([Developer.Id]))).ShouldHaveSingleItem();
    }

    /// <summary>Lo que hace el trabajo al enviar: genera el enlace y guarda su hash.</summary>
    private async Task<string> EmitirAsync(Guid id)
    {
        var token = string.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            var invitacion = await GetRequiredService<IRepository<Invitacion, Guid>>().GetAsync(id);
            token = await GetRequiredService<GestorDeInvitaciones>().EmitirTokenAsync(invitacion);
        });
        return token;
    }
}
