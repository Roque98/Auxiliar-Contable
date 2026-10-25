using System;
using System.Threading.Tasks;
using AuxiliarContable.Telegram;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Tests del vínculo con Telegram. Lo que se comprueba aquí no es el CRUD, sino las tres
/// propiedades de las que depende que esto no sea una vía de toma de control de cuentas:
/// el token caduca, sirve una sola vez, y nunca se guarda en claro.
/// </summary>
public class TelegramLinkIntegrationTests : IntegrationTestBase
{
    private readonly ITelegramLinkRepository _repositorio;
    private readonly IGuidGenerator _guids;

    public TelegramLinkIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _repositorio = GetRequiredService<ITelegramLinkRepository>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private async Task<(Guid UserId, string Token)> CrearPendienteAsync(int minutosDeVida = 10)
    {
        var userId = _guids.Create();
        var token = "token-de-prueba-" + Guid.NewGuid().ToString("N");

        var vinculo = new TelegramLink(_guids.Create(), userId);
        vinculo.IniciarVinculacion(
            TelegramLinkAppService.Hash(token),
            DateTime.UtcNow.AddMinutes(minutosDeVida));

        await WithUnitOfWorkAsync(() => _repositorio.InsertAsync(vinculo, autoSave: true));

        return (userId, token);
    }

    [Fact]
    public async Task El_token_no_se_guarda_en_claro()
    {
        var (userId, token) = await CrearPendienteAsync();

        var guardado = await WithUnitOfWorkAsync(() => _repositorio.FindByUserIdAsync(userId));

        guardado!.TokenHash.ShouldNotBe(token);
        guardado.TokenHash.ShouldBe(TelegramLinkAppService.Hash(token));
        // Quien lea la base (o una copia) no puede reconstruir el token desde el hash.
        guardado.TokenHash!.Length.ShouldBe(TelegramLinkConsts.HashLength);
    }

    [Fact]
    public async Task Un_token_valido_encuentra_su_vinculo()
    {
        var (userId, token) = await CrearPendienteAsync();

        var encontrado = await WithUnitOfWorkAsync(() =>
            _repositorio.FindByTokenHashAsync(TelegramLinkAppService.Hash(token), DateTime.UtcNow));

        encontrado.ShouldNotBeNull();
        encontrado!.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task Un_token_caducado_no_sirve()
    {
        // Nacido caducado: emula el enlace que alguien abre media hora tarde.
        var (_, token) = await CrearPendienteAsync(minutosDeVida: -1);

        var encontrado = await WithUnitOfWorkAsync(() =>
            _repositorio.FindByTokenHashAsync(TelegramLinkAppService.Hash(token), DateTime.UtcNow));

        encontrado.ShouldBeNull();
    }

    [Fact]
    public async Task Confirmar_gasta_el_token_para_que_no_se_pueda_reusar()
    {
        var (userId, token) = await CrearPendienteAsync();
        var hash = TelegramLinkAppService.Hash(token);

        await WithUnitOfWorkAsync(async () =>
        {
            var vinculo = await _repositorio.FindByTokenHashAsync(hash, DateTime.UtcNow);
            vinculo!.Confirmar(chatId: 12345, DateTime.UtcNow, "usuario_telegram");
            await _repositorio.UpdateAsync(vinculo, autoSave: true);
        });

        // Quedó vinculado...
        var vinculado = await WithUnitOfWorkAsync(() => _repositorio.FindByUserIdAsync(userId));
        vinculado!.EstaVinculado.ShouldBeTrue();
        vinculado.ChatId.ShouldBe(12345);

        // ...y el MISMO enlace ya no vale. Sin esto, alguien que interceptara el enlace
        // podría vincular TAMBIÉN su Telegram y recibir después el enlace de recuperación.
        var reintento = await WithUnitOfWorkAsync(() =>
            _repositorio.FindByTokenHashAsync(hash, DateTime.UtcNow));
        reintento.ShouldBeNull();
    }

    [Fact]
    public async Task Desvincular_deja_al_usuario_sin_chat()
    {
        var (userId, token) = await CrearPendienteAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            var vinculo = await _repositorio.FindByTokenHashAsync(
                TelegramLinkAppService.Hash(token), DateTime.UtcNow);
            vinculo!.Confirmar(chatId: 999, DateTime.UtcNow);
            vinculo.Desvincular();
            await _repositorio.UpdateAsync(vinculo, autoSave: true);
        });

        var final = await WithUnitOfWorkAsync(() => _repositorio.FindByUserIdAsync(userId));
        final!.EstaVinculado.ShouldBeFalse();
        final.ChatId.ShouldBeNull();
    }
}
