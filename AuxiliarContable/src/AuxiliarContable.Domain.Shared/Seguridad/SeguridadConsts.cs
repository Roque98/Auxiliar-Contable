namespace AuxiliarContable.Seguridad;

public static class SeguridadConsts
{
    /// <summary>
    /// Dominio de los correos PROVISIONALES: el de quien se dio de alta sin correo (ABP exige
    /// uno y único). <c>.invalid</c> está reservado (RFC 2606): no existe ni existirá, así que
    /// nada llega nunca a nadie. Aun así el emisor de correo lo descarta antes de llamar a
    /// Brevo. La persona lo cambia por el suyo desde «Seguridad de tu cuenta».
    /// </summary>
    public const string DominioProvisional = "sin-correo.invalid";

    /// <summary>
    /// Vigencia de los códigos por correo y SMS. Son los de ABP (AbpTwoFactorTokenProvider):
    /// de un solo uso, con caducidad absoluta; el host les fija esta duración.
    /// </summary>
    public const int MinutosDeValidez = 5;

    /// <summary>Espera mínima entre dos envíos al mismo canal: frena a quien pulsa «reenviar» sin parar.</summary>
    public const int SegundosEntreEnvios = 45;

    /// <summary>Envíos por canal y hora. Un SMS cuesta dinero: esto pone techo al gasto por cuenta.</summary>
    public const int MaxEnviosPorHora = 6;

    /// <summary>Códigos equivocados antes de bloquear los intentos un rato (6 dígitos = 1 entre un millón).</summary>
    public const int MaxFallos = 5;

    public const int MinutosDeBloqueo = 15;

    /// <summary>Códigos de recuperación que se generan de una vez; cada uno sirve para entrar una sola vez.</summary>
    public const int CodigosDeRecuperacion = 10;

    public static bool EsCorreoProvisional(string? correo)
    {
        return correo != null && correo.EndsWith("@" + DominioProvisional, System.StringComparison.OrdinalIgnoreCase);
    }
}

public enum CanalDeCodigo
{
    Correo = 0,
    Sms = 1,
}

public enum PropositoDeCodigo
{
    VerificarCorreo = 0,
    VerificarTelefono = 1,
    IniciarSesion = 2,
}
