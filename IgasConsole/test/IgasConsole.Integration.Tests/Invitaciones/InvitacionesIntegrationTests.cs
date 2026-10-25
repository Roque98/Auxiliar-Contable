using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Identity;
using IgasConsole.Invitaciones;
using IgasConsole.Notifications;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Invitaciones por correo contra PostgreSQL real. El correo no sale (los trabajos se
/// encolan y no se ejecutan), así que el enlace se emite a mano con el mismo
/// <see cref="GestorDeInvitaciones"/> que usa el trabajo.
/// </summary>
public class InvitacionesIntegrationTests : ConJerarquiaTestBase
{
    private readonly IInvitacionAppService _invitaciones;
    private readonly IRegistroAppService _registro;

    public InvitacionesIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _invitaciones = GetRequiredService<IInvitacionAppService>();
        _registro = GetRequiredService<IRegistroAppService>();
    }

    private Guid? _organizacion;

    /// <summary>Siempre con organización: para el equipo se ignora, para un cliente es obligatoria.</summary>
    private async Task<InvitacionDto> InvitarAsync(Volo.Abp.Identity.IdentityUser como, string email, string rol)
    {
        _organizacion ??= await CrearOrganizacionAsync();
        return await ComoAsync(como, () => _invitaciones.CreateAsync(new CrearInvitacionDto { Email = email, Rol = rol, OrganizacionId = _organizacion }));
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

    [Fact]
    public async Task Cada_uno_solo_ofrece_los_roles_que_tiene_por_debajo()
    {
        var delOwner = await ComoAsync(Owner, () => _invitaciones.GetOpcionesAsync());
        delOwner.Roles.Select(r => r.Nombre).ShouldBe([IgasRoles.Administrador, IgasRoles.Usuario], ignoreOrder: true);

        var delAdmin = await ComoAsync(Administrador, () => _invitaciones.GetOpcionesAsync());
        delAdmin.Roles.Select(r => r.Nombre).ShouldBe([IgasRoles.Usuario]);

        var delDeveloper = await ComoAsync(Developer, () => _invitaciones.GetOpcionesAsync());
        delDeveloper.Roles.Select(r => r.Nombre).ShouldContain(IgasRoles.Owner);
        delDeveloper.Roles.Select(r => r.Nombre).ShouldNotContain(IgasRoles.Developer);
    }

    [Fact]
    public async Task Nadie_invita_a_un_Developer()
    {
        (await Should.ThrowAsync<BusinessException>(() => InvitarAsync(Developer, "otro@igas.test", IgasRoles.Developer)))
            .Code.ShouldBe("IgasConsole:InvitacionRolFueraDeAlcance");
    }

    [Fact]
    public async Task Un_Administrador_no_puede_invitar_a_otro_Administrador()
    {
        var error = await Should.ThrowAsync<BusinessException>(
            () => InvitarAsync(Administrador, "nuevo@igas.test", IgasRoles.Administrador));
        error.Code.ShouldBe("IgasConsole:InvitacionRolFueraDeAlcance");
    }

    [Fact]
    public async Task Aceptar_crea_la_cuenta_con_el_rol_y_el_correo_verificado()
    {
        var invitacion = await InvitarAsync(Owner, "  Ana@IGAS.test ", IgasRoles.Administrador);
        invitacion.Email.ShouldBe("ana@igas.test");
        invitacion.EnCola.ShouldBeTrue();

        var token = await EmitirAsync(invitacion.Id);

        var publica = await WithUnitOfWorkAsync(() => _registro.ConsultarAsync(new TokenDeInvitacionDto { Token = token }));
        publica.Vigente.ShouldBeTrue();
        publica.Email.ShouldBe("ana@igas.test");
        publica.RolEtiqueta.ShouldBe("Administrador");
        publica.UsuarioSugerido.ShouldBe("ana");

        await WithUnitOfWorkAsync(() => _registro.AceptarAsync(new AceptarInvitacionDto
        {
            Token = token,
            UserName = "ana",
            Name = "Ana",
            Password = Clave
        }));

        await WithUnitOfWorkAsync(async () =>
        {
            var ana = await Usuarios.FindByEmailAsync("ana@igas.test");
            ana.ShouldNotBeNull();
            ana.EmailConfirmed.ShouldBeTrue();
            ana.Name.ShouldBe("Ana");
            (await Usuarios.GetRolesAsync(ana)).ShouldBe([IgasRoles.Administrador]);
        });

        // El enlace solo sirve una vez.
        var otra = await WithUnitOfWorkAsync(() => _registro.ConsultarAsync(new TokenDeInvitacionDto { Token = token }));
        otra.Vigente.ShouldBeFalse();
        otra.Email.ShouldBeNull();

        var lista = await ComoAsync(Owner, () => _invitaciones.GetListAsync(new GetInvitacionesInput()));
        lista.Items.Single(i => i.Id == invitacion.Id).Estado.ShouldBe(EstadoInvitacion.Aceptada);
    }

    [Fact]
    public async Task Reenviar_invalida_el_enlace_anterior()
    {
        var invitacion = await InvitarAsync(Owner, "beto@igas.test", IgasRoles.Usuario);
        var primero = await EmitirAsync(invitacion.Id);
        var segundo = await EmitirAsync(invitacion.Id);

        (await WithUnitOfWorkAsync(() => _registro.ConsultarAsync(new TokenDeInvitacionDto { Token = primero }))).Vigente.ShouldBeFalse();
        (await WithUnitOfWorkAsync(() => _registro.ConsultarAsync(new TokenDeInvitacionDto { Token = segundo }))).Vigente.ShouldBeTrue();
    }

    [Fact]
    public async Task Una_invitacion_revocada_no_deja_registrarse()
    {
        var invitacion = await InvitarAsync(Owner, "cris@igas.test", IgasRoles.Usuario);
        var token = await EmitirAsync(invitacion.Id);

        var revocada = await ComoAsync(Owner, () => _invitaciones.RevocarAsync(invitacion.Id));
        revocada.Estado.ShouldBe(EstadoInvitacion.Revocada);

        var error = await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() => _registro.AceptarAsync(
            new AceptarInvitacionDto { Token = token, UserName = "cris", Password = Clave })));
        error.Code.ShouldBe("IgasConsole:InvitacionNoValida");
    }

    [Fact]
    public async Task No_se_duplican_pendientes_ni_se_invita_a_un_correo_con_cuenta()
    {
        await InvitarAsync(Owner, "dani@igas.test", IgasRoles.Usuario);

        (await Should.ThrowAsync<BusinessException>(() => InvitarAsync(Owner, "DANI@igas.test", IgasRoles.Usuario)))
            .Code.ShouldBe("IgasConsole:InvitacionYaPendiente");

        (await Should.ThrowAsync<BusinessException>(() => InvitarAsync(Owner, "carlos@igas.test", IgasRoles.Usuario)))
            .Code.ShouldBe("IgasConsole:InvitacionCorreoConCuenta");
    }

    [Fact]
    public async Task Las_invitaciones_del_Developer_no_las_ve_el_Owner()
    {
        var delDeveloper = await InvitarAsync(Developer, "eva@igas.test", IgasRoles.Usuario);
        var delOwner = await InvitarAsync(Owner, "fer@igas.test", IgasRoles.Usuario);

        var lista = await ComoAsync(Owner, () => _invitaciones.GetListAsync(new GetInvitacionesInput()));
        lista.Items.Select(i => i.Id).ShouldBe([delOwner.Id]);

        await Should.ThrowAsync<EntityNotFoundException>(
            () => ComoAsync(Owner, () => _invitaciones.RevocarAsync(delDeveloper.Id)));
    }

    [Fact]
    public async Task Sin_correo_configurado_no_se_invita()
    {
        var correo = (CorreoDePrueba)GetRequiredService<IEstadoDelCorreo>();
        correo.PuedeEnviar = false;
        try
        {
            (await Should.ThrowAsync<BusinessException>(() => InvitarAsync(Owner, "gil@igas.test", IgasRoles.Usuario)))
                .Code.ShouldBe("IgasConsole:InvitacionCorreoNoConfigurado");
        }
        finally
        {
            correo.PuedeEnviar = true;
        }
    }
}
