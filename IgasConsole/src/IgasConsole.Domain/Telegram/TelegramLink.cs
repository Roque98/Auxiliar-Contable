using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace IgasConsole.Telegram;

/// <summary>
/// Vínculo entre un usuario de la aplicación y su chat privado de Telegram. Es lo que
/// permite mandarle mensajes: un bot de Telegram NO puede escribir a alguien que no le
/// haya hablado antes, así que hasta que la persona no arranca el bot no existe un
/// <c>ChatId</c> al que enviar nada.
/// <para>Entidad propia en vez de una propiedad extra en <c>IdentityUser</c>: no toca el
/// esquema del módulo de Identity y deja el ciclo de vida del vínculo (token pendiente,
/// confirmación, desvinculación) donde se puede razonar sobre él.</para>
/// </summary>
public class TelegramLink : Entity<Guid>
{
    public Guid UserId { get; protected set; }

    /// <summary>Chat privado con el usuario. Null mientras la vinculación esté pendiente.</summary>
    public long? ChatId { get; protected set; }

    /// <summary>
    /// SHA-256 del token de vinculación, NUNCA el token en claro. Se guarda con hash
    /// porque es una credencial de toma de control de la cuenta: quien lo lea en la base
    /// (o en una copia de seguridad) podría vincular su propio Telegram a esta cuenta y
    /// recibir después el enlace de recuperación de contraseña.
    /// </summary>
    public string? TokenHash { get; protected set; }

    public DateTime? TokenExpiresUtc { get; protected set; }

    public DateTime? LinkedUtc { get; protected set; }

    /// <summary>Nombre de usuario de Telegram, solo para poder mostrar a qué cuenta quedó
    /// vinculado. No se usa para enviar: para eso está el ChatId.</summary>
    public string? TelegramUserName { get; protected set; }

    public bool EstaVinculado => ChatId.HasValue;

    protected TelegramLink()
    {
    }

    public TelegramLink(Guid id, Guid userId) : base(id)
    {
        UserId = userId;
    }

    /// <summary>Deja una vinculación pendiente. Reemplaza cualquier token anterior: pedir
    /// un enlace nuevo invalida el viejo.</summary>
    public void IniciarVinculacion(string tokenHash, DateTime expiraUtc)
    {
        ExigirUtc(expiraUtc, nameof(expiraUtc));

        TokenHash = Check.NotNullOrWhiteSpace(
            tokenHash, nameof(tokenHash), TelegramLinkConsts.HashLength);
        TokenExpiresUtc = expiraUtc;
    }

    /// <summary>
    /// Confirma la vinculación. El token se limpia SIEMPRE: es de un solo uso, así que
    /// aunque alguien lo hubiera interceptado ya no le sirve.
    /// </summary>
    public void Confirmar(long chatId, DateTime ahoraUtc, string? telegramUserName = null)
    {
        ExigirUtc(ahoraUtc, nameof(ahoraUtc));

        ChatId = chatId;
        LinkedUtc = ahoraUtc;
        TelegramUserName = telegramUserName;
        TokenHash = null;
        TokenExpiresUtc = null;
    }

    public void Desvincular()
    {
        ChatId = null;
        LinkedUtc = null;
        TelegramUserName = null;
        TokenHash = null;
        TokenExpiresUtc = null;
    }

    public bool TokenVigente(DateTime ahoraUtc)
    {
        return TokenHash != null && TokenExpiresUtc.HasValue && TokenExpiresUtc.Value > ahoraUtc;
    }

    private static void ExigirUtc(DateTime valor, string nombre)
    {
        if (valor.Kind != DateTimeKind.Utc)
        {
            throw new AbpException(
                $"TelegramLink.{nombre} debe ser UTC: la columna es timestamptz y Npgsql " +
                "lanza en tiempo de ejecución si el Kind no es Utc.");
        }
    }
}
