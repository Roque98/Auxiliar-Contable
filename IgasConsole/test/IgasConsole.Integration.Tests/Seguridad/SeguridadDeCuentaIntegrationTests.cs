using System.Threading.Tasks;
using IgasConsole.Identity;
using IgasConsole.Seguridad;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Verificar correo y teléfono con código, y activar la verificación en dos pasos. El código
/// no sale de verdad: lo guarda <see cref="EnviadorDePrueba"/>.
/// </summary>
public class SeguridadDeCuentaIntegrationTests : ConJerarquiaTestBase
{
    private readonly ISeguridadDeCuentaAppService _servicio;
    private readonly EnviadorDePrueba _enviador;

    public SeguridadDeCuentaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _servicio = GetRequiredService<ISeguridadDeCuentaAppService>();
        _enviador = (EnviadorDePrueba)GetRequiredService<IEnviadorDeCodigos>();
    }

    [Fact]
    public async Task Quien_se_dio_de_alta_sin_correo_registra_el_suyo_con_un_codigo()
    {
        var rafael = await CrearUsuarioAsync("rafael", IgasRoles.Owner);
        await WithUnitOfWorkAsync(async () =>
        {
            var u = await Usuarios.GetByIdAsync(rafael.Id);
            (await Usuarios.SetEmailAsync(u, "rafael@" + SeguridadConsts.DominioProvisional)).CheckErrors();
        });

        var antes = await ComoAsync(rafael, () => _servicio.GetAsync());
        antes.Correo.ShouldBeNull();
        antes.CorreoConfirmado.ShouldBeFalse();
        antes.Pendientes.ShouldBe(3);

        await ComoAsync(rafael, () => _servicio.EnviarCodigoCorreoAsync(new CorreoDto { Correo = " Rafael@IGAS.test " }));
        _enviador.UltimoDestino.ShouldBe("rafael@igas.test");

        var despues = await ComoAsync(rafael, () => _servicio.ConfirmarCorreoAsync(
            new ConfirmarCorreoDto { Correo = "rafael@igas.test", Codigo = _enviador.UltimoCodigo! }));

        despues.Correo.ShouldBe("rafael@igas.test");
        despues.CorreoConfirmado.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_codigo_de_otro_correo_no_sirve()
    {
        await ComoAsync(Usuario, () => _servicio.EnviarCodigoCorreoAsync(new CorreoDto { Correo = "uno@igas.test" }));

        var error = await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario, () => _servicio.ConfirmarCorreoAsync(
            new ConfirmarCorreoDto { Correo = "otro@igas.test", Codigo = _enviador.UltimoCodigo! })));
        error.Code.ShouldBe("IgasConsole:CodigoIncorrecto");
    }

    [Fact]
    public async Task No_se_puede_usar_el_correo_de_otra_cuenta()
    {
        var error = await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario,
            () => _servicio.EnviarCodigoCorreoAsync(new CorreoDto { Correo = "laura@igas.test" })));
        error.Code.ShouldBe("IgasConsole:InvitacionCorreoConCuenta");
    }

    [Fact]
    public async Task El_telefono_se_confirma_con_el_codigo_del_sms()
    {
        await ComoAsync(Usuario, () => _servicio.EnviarCodigoTelefonoAsync(new TelefonoDto { Telefono = "+52 55 1234 5678" }));
        _enviador.UltimoDestino.ShouldBe("+525512345678");

        var estado = await ComoAsync(Usuario, () => _servicio.ConfirmarTelefonoAsync(
            new ConfirmarTelefonoDto { Telefono = "+52 55 1234 5678", Codigo = _enviador.UltimoCodigo! }));

        estado.Telefono.ShouldBe("+525512345678");
        estado.TelefonoConfirmado.ShouldBeTrue();
    }

    [Fact]
    public async Task Sin_sms_el_telefono_se_guarda_sin_verificar()
    {
        _enviador.SmsDisponible = false;
        try
        {
            (await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario,
                () => _servicio.EnviarCodigoTelefonoAsync(new TelefonoDto { Telefono = "+525512345678" }))))
                .Code.ShouldBe("IgasConsole:SmsNoDisponible");

            var estado = await ComoAsync(Usuario, () => _servicio.GuardarTelefonoAsync(new TelefonoDto { Telefono = "+52 55 8765 4321" }));
            estado.Telefono.ShouldBe("+525587654321");
            estado.TelefonoConfirmado.ShouldBeFalse();
        }
        finally
        {
            _enviador.SmsDisponible = true;
        }
    }

    [Fact]
    public async Task Tras_varios_codigos_malos_se_bloquean_los_intentos()
    {
        await ComoAsync(Administrador, () => _servicio.EnviarCodigoCorreoAsync(new CorreoDto { Correo = "nueva@igas.test" }));

        for (var i = 0; i < SeguridadConsts.MaxFallos; i++)
        {
            await Should.ThrowAsync<BusinessException>(() => ComoAsync(Administrador, () => _servicio.ConfirmarCorreoAsync(
                new ConfirmarCorreoDto { Correo = "nueva@igas.test", Codigo = "000000" })));
        }

        // Ni el código bueno entra mientras dura el bloqueo.
        (await Should.ThrowAsync<BusinessException>(() => ComoAsync(Administrador, () => _servicio.ConfirmarCorreoAsync(
            new ConfirmarCorreoDto { Correo = "nueva@igas.test", Codigo = _enviador.UltimoCodigo! }))))
            .Code.ShouldBe("IgasConsole:CodigoDemasiadosFallos");
    }

    [Fact]
    public async Task No_se_piden_codigos_seguidos()
    {
        await ComoAsync(Owner, () => _servicio.EnviarCodigoCorreoAsync(new CorreoDto { Correo = "tio.nuevo@igas.test" }));

        (await Should.ThrowAsync<BusinessException>(() => ComoAsync(Owner,
            () => _servicio.EnviarCodigoCorreoAsync(new CorreoDto { Correo = "tio.nuevo@igas.test" }))))
            .Code.ShouldBe("IgasConsole:CodigoEsperaEntreEnvios");
    }

    [Fact]
    public async Task La_verificacion_en_dos_pasos_exige_un_metodo_verificado()
    {
        // Los usuarios de prueba nacen con el correo sin confirmar.
        (await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario,
            () => _servicio.SetSegundoFactorAsync(new SegundoFactorDto { Activo = true }))))
            .Code.ShouldBe("IgasConsole:SegundoFactorSinMetodo");

        await WithUnitOfWorkAsync(async () =>
        {
            var u = await Usuarios.GetByIdAsync(Usuario.Id);
            u.SetEmailConfirmed(true);
            (await Usuarios.UpdateAsync(u)).CheckErrors();
        });

        var estado = await ComoAsync(Usuario, () => _servicio.SetSegundoFactorAsync(new SegundoFactorDto { Activo = true }));
        estado.SegundoFactorActivo.ShouldBeTrue();
    }

    private async Task ActivarDosPasosAsync(Volo.Abp.Identity.IdentityUser quien)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var u = await Usuarios.GetByIdAsync(quien.Id);
            u.SetEmailConfirmed(true);
            (await Usuarios.UpdateAsync(u)).CheckErrors();
            (await Usuarios.SetTwoFactorEnabledAsync(u, true)).CheckErrors();
        });
    }

    [Fact]
    public async Task Los_codigos_de_recuperacion_exigen_los_dos_pasos_y_se_cuentan()
    {
        (await Should.ThrowAsync<BusinessException>(() => ComoAsync(Usuario,
            () => _servicio.GenerarCodigosDeRecuperacionAsync())))
            .Code.ShouldBe("IgasConsole:RecuperacionSinSegundoFactor");

        await ActivarDosPasosAsync(Usuario);

        var codigos = await ComoAsync(Usuario, () => _servicio.GenerarCodigosDeRecuperacionAsync());
        codigos.Codigos.Count.ShouldBe(SeguridadConsts.CodigosDeRecuperacion);
        codigos.Codigos.ShouldBeUnique();

        (await ComoAsync(Usuario, () => _servicio.GetAsync())).CodigosDeRecuperacionRestantes
            .ShouldBe(SeguridadConsts.CodigosDeRecuperacion);

        // Uno usado deja de valer y ya no cuenta.
        await WithUnitOfWorkAsync(async () =>
        {
            var u = await Usuarios.GetByIdAsync(Usuario.Id);
            (await Usuarios.RedeemTwoFactorRecoveryCodeAsync(u, codigos.Codigos[0])).Succeeded.ShouldBeTrue();
            (await Usuarios.RedeemTwoFactorRecoveryCodeAsync(u, codigos.Codigos[0])).Succeeded.ShouldBeFalse();
        });
        (await ComoAsync(Usuario, () => _servicio.GetAsync())).CodigosDeRecuperacionRestantes
            .ShouldBe(SeguridadConsts.CodigosDeRecuperacion - 1);
    }

    [Fact]
    public async Task Quien_gestiona_a_alguien_puede_quitarle_los_dos_pasos()
    {
        var admin = GetRequiredService<ISeguridadDeUsuariosAppService>();
        await ActivarDosPasosAsync(Administrador);

        (await ComoAsync(Owner, () => admin.GetAsync(Administrador.Id))).SegundoFactorActivo.ShouldBeTrue();
        var despues = await ComoAsync(Owner, () => admin.QuitarSegundoFactorAsync(Administrador.Id));
        despues.SegundoFactorActivo.ShouldBeFalse();
    }

    [Fact]
    public async Task Nadie_quita_los_dos_pasos_a_quien_no_alcanza_ni_a_si_mismo()
    {
        var admin = GetRequiredService<ISeguridadDeUsuariosAppService>();
        await ActivarDosPasosAsync(Owner);

        // Un Administrador no alcanza al Owner: 404, como en el resto de la jerarquía.
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(
            () => ComoAsync(Administrador, () => admin.QuitarSegundoFactorAsync(Owner.Id)));
        // Ni el Owner a sí mismo desde aquí: lo suyo va por «Seguridad de tu cuenta».
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(
            () => ComoAsync(Owner, () => admin.QuitarSegundoFactorAsync(Owner.Id)));

        await WithUnitOfWorkAsync(async () => (await Usuarios.GetByIdAsync(Owner.Id)).TwoFactorEnabled.ShouldBeTrue());
    }
}
