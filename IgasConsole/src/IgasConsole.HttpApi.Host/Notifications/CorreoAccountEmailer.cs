using System.Threading.Tasks;
using IgasConsole.BackgroundJobs;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp.Account.Emailing;
using Volo.Abp.Account.Localization;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TextTemplating;
using Volo.Abp.UI.Navigation.Urls;

namespace IgasConsole.Notifications;

/// <summary>
/// El enlace de «olvidé mi contraseña» sale por correo (Brevo), pero ENCOLADO y sin el
/// token: ver <see cref="EnviarEnlaceRecuperacionJob"/>.
///
/// <para>ABP lo manda dentro de la petición. Aquí se encola porque así la pantalla responde
/// al instante, un fallo de Brevo se reintenta solo, y el tiempo de respuesta no delata si la
/// cuenta existe (con envío en línea, «existe» tardaría lo que tarda Brevo).</para>
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAccountEmailer))]
public class CorreoAccountEmailer : AccountEmailer
{
    private readonly IBackgroundJobManager _jobs;
    private readonly ILogger<CorreoAccountEmailer> _logger;

    public CorreoAccountEmailer(
        IEmailSender emailSender,
        ITemplateRenderer templateRenderer,
        IStringLocalizer<AccountResource> stringLocalizer,
        IAppUrlProvider appUrlProvider,
        ICurrentTenant currentTenant,
        IBackgroundJobManager jobs,
        ILogger<CorreoAccountEmailer> logger)
        : base(emailSender, templateRenderer, stringLocalizer, appUrlProvider, currentTenant)
    {
        _jobs = jobs;
        _logger = logger;
    }

    /// <summary>
    /// ⚠️ <paramref name="resetToken"/> se IGNORA a propósito: los argumentos de un trabajo
    /// se guardan en <c>AbpBackgroundJobs</c> y ese token es una credencial. El trabajo genera
    /// el suyo justo antes de enviar (los de Identity van firmados y pueden convivir varios).
    /// </summary>
    public override async Task SendPasswordResetLinkAsync(
        IdentityUser user,
        string resetToken,
        string appName,
        string? returnUrl = null,
        string? returnUrlHash = null)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            // Sin excepción: la pantalla responde igual exista o no la cuenta.
            _logger.LogWarning(
                "Recuperación pedida para el usuario {UserId}, que no tiene correo.", user.Id);
            return;
        }

        await _jobs.EnqueueAsync(new EnviarEnlaceRecuperacionArgs
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            AppName = appName,
            ReturnUrl = returnUrl,
            ReturnUrlHash = returnUrlHash
        });
    }
}
