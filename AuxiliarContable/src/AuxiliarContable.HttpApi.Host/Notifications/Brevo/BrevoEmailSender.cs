using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Emailing;
using Volo.Abp.MultiTenancy;

namespace AuxiliarContable.Notifications.Brevo;

/// <summary>
/// El <see cref="IEmailSender"/> de ABP sobre la API de Brevo. Al sustituir la pieza de ABP
/// y no inventar otra, todo lo que ya manda correo en ABP —recuperar contraseña, confirmar
/// la dirección— sale por aquí sin tocarlo.
///
/// <para>El remitente es siempre el de <see cref="BrevoOptions"/>, no el <c>From</c> del
/// mensaje: Brevo solo acepta remitentes de un dominio verificado, y el <c>From</c> por
/// defecto de ABP (<c>noreply@abp.io</c>) no lo es.</para>
///
/// <para>⚠️ Sin API key NO lanza: registra un aviso y sigue. Es lo que pasa en desarrollo y
/// en los tests. En producción el <c>env</c> de plantilla trae <c>CAMBIAME</c>, que Brevo
/// rechaza con un 401 visible — no queda un «correo que nunca salió» en silencio.</para>
/// </summary>
public class BrevoEmailSender : EmailSenderBase
{
    private readonly BrevoClient _brevo;
    private readonly BrevoOptions _opciones;

    public BrevoEmailSender(
        ICurrentTenant currentTenant,
        IEmailSenderConfiguration configuration,
        IBackgroundJobManager backgroundJobManager,
        BrevoClient brevo,
        IOptions<BrevoOptions> opciones)
        : base(currentTenant, configuration, backgroundJobManager)
    {
        _brevo = brevo;
        _opciones = opciones.Value;
    }

    protected override async Task SendEmailAsync(MailMessage mail)
    {
        if (!_opciones.TieneCredenciales)
        {
            Logger.LogWarning(
                "Brevo sin configurar: el correo «{Asunto}» para {Destinatarios} NO se ha enviado.",
                mail.Subject, mail.To.ToString());
            return;
        }

        foreach (var destinatario in mail.To)
        {
            // Correo provisional de quien se dio de alta sin correo: no existe (RFC 2606).
            if (AuxiliarContable.Seguridad.SeguridadConsts.EsCorreoProvisional(destinatario.Address))
            {
                Logger.LogInformation("Correo «{Asunto}» descartado: el destinatario no tiene correo real.", mail.Subject);
                continue;
            }

            await _brevo.EnviarCorreoAsync(destinatario.Address, mail.Subject, mail.Body, mail.IsBodyHtml);
        }
    }
}
