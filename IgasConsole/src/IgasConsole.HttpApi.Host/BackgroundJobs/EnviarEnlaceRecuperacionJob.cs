using System;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Account.Emailing;
using Volo.Abp.Account.Emailing.Templates;
using IgasConsole.Notifications;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Uow;

namespace IgasConsole.BackgroundJobs;

/// <summary>
/// Manda por correo (Brevo) el enlace para restablecer la contraseña, con la plantilla y los
/// textos de ABP (<see cref="AccountEmailTemplates.PasswordResetLink"/>).
///
/// <para>Fuera de la petición HTTP: la pantalla contesta de inmediato, y si Brevo falla el
/// envío se reintenta solo (el trabajo lanza).</para>
/// </summary>
public class EnviarEnlaceRecuperacionJob : AsyncBackgroundJob<EnviarEnlaceRecuperacionArgs>, ITransientDependency
{
    private readonly IdentityUserManager _usuarios;
    private readonly IEmailSender _correo;
    private readonly PlantillasDeCorreo _plantillas;
    private readonly IAppUrlProvider _urls;
    private readonly ICurrentTenant _tenantActual;
    private readonly IUnitOfWorkManager _unidadesDeTrabajo;

    public EnviarEnlaceRecuperacionJob(
        IdentityUserManager usuarios,
        IEmailSender correo,
        PlantillasDeCorreo plantillas,
        IAppUrlProvider urls,
        ICurrentTenant tenantActual,
        IUnitOfWorkManager unidadesDeTrabajo)
    {
        _usuarios = usuarios;
        _correo = correo;
        _plantillas = plantillas;
        _urls = urls;
        _tenantActual = tenantActual;
        _unidadesDeTrabajo = unidadesDeTrabajo;
    }

    public override async Task ExecuteAsync(EnviarEnlaceRecuperacionArgs args)
    {
        // ⚠️ La unidad de trabajo hay que ABRIRLA A MANO. A diferencia de un application
        // service, un trabajo en segundo plano NO viene envuelto en una: ABP lo dice en el
        // propio AsyncBackgroundJob ("TODO: Add UOW..."). Sin esto, la primera consulta
        // revienta con «A DbContext can only be created inside a unit of work!» — que es
        // exactamente lo que pasó la primera vez que se ejecutó esto de verdad.
        using var unidad = _unidadesDeTrabajo.Begin(requiresNew: true);

        using (_tenantActual.Change(args.TenantId))
        {
            var usuario = await _usuarios.FindByIdAsync(args.UserId.ToString());
            if (usuario == null)
            {
                // Borrado entre la petición y el envío. No es un error que merezca
                // reintentos: no va a aparecer de nuevo.
                Logger.LogWarning("El usuario {UserId} ya no existe; no hay a quién enviar el enlace.", args.UserId);
                return;
            }

            if (string.IsNullOrWhiteSpace(usuario.Email))
            {
                // Le quitaron el correo después de pedirlo. No tiene arreglo reintentando.
                Logger.LogWarning("El usuario {UserId} ya no tiene correo.", usuario.Id);
                return;
            }

            // El token nace AQUÍ, no en la petición: así su ventana de validez empieza a
            // contar cuando el mensaje sale de verdad, y nunca pasa por la tabla de jobs.
            var token = await _usuarios.GeneratePasswordResetTokenAsync(usuario);

            var url = await _urls.GetResetPasswordUrlAsync(args.AppName);

            var enlace = $"{url}?userId={usuario.Id}" +
                         $"&__tenant={usuario.TenantId}" +
                         $"&resetToken={UrlEncoder.Default.Encode(token)}";

            if (!string.IsNullOrEmpty(args.ReturnUrl))
            {
                enlace += "&returnUrl=" + args.ReturnUrl;
            }

            if (!string.IsNullOrEmpty(args.ReturnUrlHash))
            {
                enlace += "&returnUrlHash=" + args.ReturnUrlHash;
            }

            // La plantilla es la de ZIA (Emails/RestablecerContrasena.tpl), que sustituye a
            // la de ABP con el mismo nombre; el modelo sigue siendo el de ABP ({ link }).
            var cuerpo = await _plantillas.RenderAsync(
                AccountEmailTemplates.PasswordResetLink,
                new { link = enlace },
                "Elige una contraseña nueva para tu cuenta de ZIA.");

            // Lanza si Brevo lo rechaza: así ABP reintenta el trabajo.
            await _correo.SendAsync(usuario.Email, "Restablece tu contraseña de ZIA", cuerpo);
        }

        await unidad.CompleteAsync();
    }
}
