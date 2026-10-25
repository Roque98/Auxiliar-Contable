using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Telegram;

public class TelegramLinkStatusDto
{
    public bool IsLinked { get; set; }

    public DateTime? LinkedUtc { get; set; }

    /// <summary>A qué cuenta de Telegram quedó vinculado, para que se pueda reconocer.</summary>
    public string? TelegramUserName { get; set; }

    /// <summary>False si no hay bot configurado: sin bot no hay nada que vincular.</summary>
    public bool BotAvailable { get; set; }

    /// <summary>
    /// Si al iniciar sesión se pide además un código enviado por Telegram.
    /// <para>Solo puede estar activo con la cuenta vinculada: sin canal no habría por dónde
    /// mandar el código.</para>
    /// </summary>
    public bool TwoFactorEnabled { get; set; }
}

public class StartTelegramLinkResultDto
{
    /// <summary>
    /// Enlace t.me/&lt;bot&gt;?start=&lt;token&gt;. Se devuelve UNA sola vez y lleva el token
    /// en claro: es una credencial temporal, no se guarda ni se puede volver a consultar.
    /// </summary>
    public string DeepLink { get; set; } = string.Empty;

    public DateTime ExpiresUtc { get; set; }

    public int ValidMinutes { get; set; }
}

/// <summary>
/// Vinculación del Telegram del USUARIO ACTUAL. No lleva permiso propio a propósito:
/// cualquiera que haya iniciado sesión puede vincular su propia cuenta, y solo la suya
/// (el id de usuario sale de la sesión, nunca de la petición).
/// </summary>
public interface ITelegramLinkAppService : IApplicationService
{
    Task<TelegramLinkStatusDto> GetStatusAsync();

    Task<StartTelegramLinkResultDto> StartAsync();

    Task UnlinkAsync();

    /// <summary>
    /// Activa o desactiva el segundo factor por Telegram para el usuario actual.
    /// <para>Activarlo exige tener la cuenta vinculada.</para>
    /// </summary>
    Task<TelegramLinkStatusDto> SetTwoFactorAsync(bool enabled);
}
