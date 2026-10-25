using System;
using System.Threading.Tasks;
using IgasConsole.Telegram;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Xunit;

using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace IgasConsole;

/// <summary>
/// Segundo factor por Telegram. Lo que se prueba es la regla que lo sostiene: el segundo
/// factor y el vínculo no pueden ir por separado, porque el vínculo ES el canal por el que
/// llega el código.
/// </summary>
public class TwoFactorIntegrationTests : IntegrationTestBase
{
    private readonly ITelegramLinkAppService _telegram;
    private readonly ITelegramLinkRepository _vinculos;
    private readonly IdentityUserManager _usuarios;
    private readonly IGuidGenerator _guids;

    /// <summary>El mismo id que finge FakeCurrentPrincipalAccessor.</summary>
    private static readonly Guid UsuarioActual = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    public TwoFactorIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _telegram = GetRequiredService<ITelegramLinkAppService>();
        _vinculos = GetRequiredService<ITelegramLinkRepository>();
        _usuarios = GetRequiredService<IdentityUserManager>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    /// <summary>Crea el usuario de la sesión de prueba, ya vinculado a un chat.</summary>
    private async Task CrearUsuarioVinculadoAsync(bool vinculado = true)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var usuario = new IdentityUser(UsuarioActual, "admin", "admin@abp.io");
            (await _usuarios.CreateAsync(usuario)).CheckErrors();

            var vinculo = new TelegramLink(_guids.Create(), UsuarioActual);
            if (vinculado)
            {
                vinculo.Confirmar(123456789, DateTime.UtcNow, "usuario_de_prueba");
            }

            await _vinculos.InsertAsync(vinculo, autoSave: true);
        });
    }

    [Fact]
    public async Task No_se_puede_activar_sin_Telegram_vinculado()
    {
        await CrearUsuarioVinculadoAsync(vinculado: false);

        // Dejar activarlo sin canal seria lo peor de los dos mundos: el usuario creeria
        // tener un segundo factor, Identity no tendria proveedor valido con el que pedirlo
        // y entraria solo con la contrasena. Falsa confianza.
        var ex = await Should.ThrowAsync<UserFriendlyException>(() => _telegram.SetTwoFactorAsync(true));

        ex.Message.ShouldContain("vincular tu Telegram");

        var estado = await _telegram.GetStatusAsync();
        estado.TwoFactorEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Con_Telegram_vinculado_se_activa_y_se_desactiva()
    {
        await CrearUsuarioVinculadoAsync();

        var tras = await _telegram.SetTwoFactorAsync(true);
        tras.TwoFactorEnabled.ShouldBeTrue();
        (await _telegram.GetStatusAsync()).TwoFactorEnabled.ShouldBeTrue();

        var apagado = await _telegram.SetTwoFactorAsync(false);
        apagado.TwoFactorEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Desvincular_APAGA_el_segundo_factor()
    {
        // EL TEST QUE IMPORTA. Sin esto la seguridad se degrada en silencio: al desvincular,
        // el proveedor de Telegram deja de ser valido, Identity se queda sin proveedores y
        // NO pide el segundo factor — pero el flag seguiria en true y el usuario seguiria
        // leyendo "activado" mientras entra solo con la contrasena.
        await CrearUsuarioVinculadoAsync();
        await _telegram.SetTwoFactorAsync(true);

        await _telegram.UnlinkAsync();

        var estado = await _telegram.GetStatusAsync();
        estado.IsLinked.ShouldBeFalse();
        estado.TwoFactorEnabled.ShouldBeFalse();

        // Y en la fuente de verdad, no solo en el DTO.
        await WithUnitOfWorkAsync(async () =>
        {
            var usuario = await _usuarios.GetByIdAsync(UsuarioActual);
            usuario.TwoFactorEnabled.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task El_estado_arranca_apagado()
    {
        await CrearUsuarioVinculadoAsync();

        (await _telegram.GetStatusAsync()).TwoFactorEnabled.ShouldBeFalse();
    }
}
