namespace AuxiliarContable.Notifications.Brevo;

/// <summary>
/// Configuración de Brevo, sección <c>Brevo</c>. La API key NUNCA va en
/// <c>appsettings*.json</c>: en desarrollo va en user-secrets y en producción en
/// <c>/etc/auxiliar-contable/env</c> como <c>Brevo__ApiKey</c>.
///
/// <para>Sin API key no se envía nada y cada intento queda en el log como aviso. Es lo que
/// pasa en desarrollo y en los tests, a propósito: un test no debe mandar correos reales.</para>
/// </summary>
public class BrevoOptions
{
    public const string Seccion = "Brevo";

    public string BaseUrl { get; set; } = "https://api.brevo.com/v3/";

    public string? ApiKey { get; set; }

    /// <summary>Remitente de los correos. Tiene que ser de un dominio verificado en Brevo.</summary>
    public string? SenderEmail { get; set; }

    public string SenderName { get; set; } = "Auxiliar contable";

    /// <summary>Remitente de SMS: hasta 11 caracteres alfanuméricos o 15 dígitos.</summary>
    public string SmsSender { get; set; } = "AuxContable";

    /// <summary>
    /// SMS encendidos. Apagado por defecto: el plan gratuito de Brevo no trae créditos de SMS y
    /// cada intento fallaría. Se enciende (<c>Brevo__SmsHabilitado=true</c>) al comprarlos;
    /// hasta entonces el teléfono se guarda sin verificar y el segundo factor va por correo.
    /// </summary>
    public bool SmsHabilitado { get; set; }

    /// <summary>Número de WhatsApp Business dado de alta en Brevo, con código de país.</summary>
    public string? WhatsAppSenderNumber { get; set; }

    public bool TieneCredenciales => !string.IsNullOrWhiteSpace(ApiKey);
}
