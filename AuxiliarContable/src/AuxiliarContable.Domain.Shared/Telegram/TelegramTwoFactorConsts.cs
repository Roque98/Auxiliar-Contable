namespace AuxiliarContable.Telegram;

public static class TelegramTwoFactorConsts
{
    /// <summary>
    /// Nombre con el que se registra el proveedor de segundo factor en
    /// <c>IdentityOptions.Tokens.ProviderMap</c>.
    /// <para>Va en una constante porque aparece en tres sitios que TIENEN que coincidir:
    /// el registro, la generación del código y su validación. Un nombre distinto en
    /// cualquiera de ellos no falla al compilar: simplemente el código nunca valida.</para>
    /// </summary>
    public const string ProviderName = "Telegram";

    /// <summary>Minutos que vale un código. Informativo, para poder decírselo al usuario:
    /// el valor real lo fija el paso de tiempo del proveedor TOTP de ASP.NET Identity.</summary>
    public const int CodigoValidezMinutos = 3;
}
