using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace IgasConsole.Telegram;

/// <summary>
/// Vincula la cuenta del usuario actual con su chat de Telegram, para poder mandarle
/// cosas como el enlace de recuperación de contraseña.
/// </summary>
[Authorize]
public class TelegramLinkAppService : IgasConsoleAppService, ITelegramLinkAppService
{
    private readonly ITelegramLinkRepository _repositorio;
    private readonly ITelegramMessenger _mensajero;
    private readonly IGuidGenerator _guids;
    private readonly IdentityUserManager _usuarios;

    public TelegramLinkAppService(
        ITelegramLinkRepository repositorio,
        ITelegramMessenger mensajero,
        IGuidGenerator guids,
        IdentityUserManager usuarios)
    {
        _repositorio = repositorio;
        _mensajero = mensajero;
        _guids = guids;
        _usuarios = usuarios;
    }

    public async Task<TelegramLinkStatusDto> GetStatusAsync()
    {
        var userId = UsuarioActual();
        var vinculo = await _repositorio.FindByUserIdAsync(userId);

        // IsBotConfiguredAsync y NO GetBotUserNameAsync: esto solo necesita saber SI hay
        // bot, y preguntárselo a Telegram hacía que esta pantalla dependiera de su
        // latencia. En producción llegó a tardar 15 segundos y se quedaba en "Consultando…".
        var hayBot = await _mensajero.IsBotConfiguredAsync();

        var usuario = await _usuarios.GetByIdAsync(userId);

        return new TelegramLinkStatusDto
        {
            IsLinked = vinculo?.EstaVinculado ?? false,
            LinkedUtc = vinculo?.LinkedUtc,
            TelegramUserName = vinculo?.TelegramUserName,
            BotAvailable = hayBot,
            TwoFactorEnabled = usuario.TwoFactorEnabled
        };
    }

    public async Task<TelegramLinkStatusDto> SetTwoFactorAsync(bool enabled)
    {
        var userId = UsuarioActual();

        if (enabled)
        {
            var vinculo = await _repositorio.FindByUserIdAsync(userId);
            if (vinculo?.EstaVinculado != true)
            {
                // Se comprueba ANTES de activarlo. Si se dejara activar sin vínculo, el
                // usuario creería tener un segundo factor que Identity no puede pedir
                // (no hay proveedor válido) y entraría solo con la contraseña: peor que
                // no tenerlo, porque además da falsa confianza.
                throw new UserFriendlyException(
                    "Antes de activar el segundo factor tienes que vincular tu Telegram: " +
                    "es por donde llega el código.");
            }
        }

        var usuario = await _usuarios.GetByIdAsync(userId);
        (await _usuarios.SetTwoFactorEnabledAsync(usuario, enabled)).CheckErrors();

        return await GetStatusAsync();
    }

    public async Task<StartTelegramLinkResultDto> StartAsync()
    {
        var bot = await _mensajero.GetBotUserNameAsync();
        if (bot.IsNullOrWhiteSpace())
        {
            throw new UserFriendlyException(
                "No hay un bot de Telegram configurado. Configúralo en la página de métricas antes de vincular.");
        }

        var userId = UsuarioActual();
        var vinculo = await _repositorio.FindByUserIdAsync(userId);

        if (vinculo == null)
        {
            vinculo = new TelegramLink(_guids.Create(), userId);
            await _repositorio.InsertAsync(vinculo, autoSave: true);
        }

        // El token viaja al usuario y se guarda SOLO su hash: ver TelegramLink.TokenHash.
        var token = GenerarToken();
        var expira = DateTime.UtcNow.AddMinutes(TelegramLinkConsts.TokenValidezMinutos);

        vinculo.IniciarVinculacion(Hash(token), expira);
        await _repositorio.UpdateAsync(vinculo, autoSave: true);

        return new StartTelegramLinkResultDto
        {
            DeepLink = $"https://t.me/{bot}?start={token}",
            ExpiresUtc = expira,
            ValidMinutes = TelegramLinkConsts.TokenValidezMinutos
        };
    }

    public async Task UnlinkAsync()
    {
        var userId = UsuarioActual();

        // ⚠️ APAGAR EL SEGUNDO FACTOR AL DESVINCULAR NO ES UN EXTRA, ES LO QUE EVITA UNA
        // DEGRADACIÓN SILENCIOSA. Identity solo pide el segundo factor si hay algún
        // proveedor válido, y el de Telegram deja de serlo en cuanto no hay chat. Si el
        // flag se quedara puesto, el usuario seguiría viendo "activado" mientras entra
        // solo con la contraseña. Mejor apagarlo de verdad y que se note.
        var usuario = await _usuarios.GetByIdAsync(userId);
        if (usuario.TwoFactorEnabled)
        {
            (await _usuarios.SetTwoFactorEnabledAsync(usuario, false)).CheckErrors();
        }

        var vinculo = await _repositorio.FindByUserIdAsync(userId);
        if (vinculo == null)
        {
            return;
        }

        vinculo.Desvincular();
        await _repositorio.UpdateAsync(vinculo, autoSave: true);
    }

    /// <summary>
    /// El id SIEMPRE de la sesión, nunca de la petición: si viniera por parámetro,
    /// cualquiera podría vincular su Telegram a la cuenta de otro.
    /// </summary>
    private Guid UsuarioActual()
    {
        return CurrentUser.GetId();
    }

    /// <summary>Token aleatorio en base64url, que es lo que admite el payload de un deep
    /// link de Telegram (A-Z a-z 0-9 _ -).</summary>
    private static string GenerarToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TelegramLinkConsts.TokenBytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
