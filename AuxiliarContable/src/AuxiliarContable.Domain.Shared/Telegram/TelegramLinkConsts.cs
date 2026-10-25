namespace AuxiliarContable.Telegram;

public static class TelegramLinkConsts
{
    /// <summary>
    /// Minutos que vale un token de vinculación. Corto A PROPÓSITO: ese token es una
    /// credencial. Quien lo tenga puede vincular SU Telegram a la cuenta de otro y, a
    /// partir de ahí, pedir el enlace de recuperación de contraseña y recibirlo él.
    /// </summary>
    public const int TokenValidezMinutos = 10;

    /// <summary>Longitud del token en bytes antes de codificar. 32 bytes = 256 bits.</summary>
    public const int TokenBytes = 32;

    /// <summary>El payload de un deep link de Telegram admite 64 caracteres como máximo.</summary>
    public const int MaxTokenLength = 64;

    /// <summary>SHA-256 en hexadecimal.</summary>
    public const int HashLength = 64;
}
