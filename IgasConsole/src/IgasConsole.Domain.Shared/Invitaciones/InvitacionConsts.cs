namespace IgasConsole.Invitaciones;

public static class InvitacionConsts
{
    public const int MaxEmailLength = 256;

    public const int MaxRolLength = 256;

    /// <summary>SHA-256 en hexadecimal: 64 caracteres.</summary>
    public const int TokenHashLength = 64;

    /// <summary>
    /// Días que vale un enlace. Una semana da margen a quien no mira el correo a diario, y
    /// reenviar la invitación genera uno nuevo (e invalida el anterior).
    /// </summary>
    public const int DiasDeVigencia = 7;

    /// <summary>Máximo de envíos de una misma invitación: corta el uso de esto como spam.</summary>
    public const int MaxEnvios = 5;
}

/// <summary>El estado se deduce de las fechas: no se guarda, así no se desincroniza.</summary>
public enum EstadoInvitacion
{
    Pendiente = 0,
    Aceptada = 1,
    Revocada = 2,
    Vencida = 3
}
