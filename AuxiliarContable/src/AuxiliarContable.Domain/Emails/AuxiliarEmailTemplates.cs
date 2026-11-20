using Volo.Abp.Emailing.Templates;
using Volo.Abp.TextTemplating;

namespace AuxiliarContable.Emails;

/// <summary>
/// Los correos de la consola. Las plantillas (Scriban) están en <c>AuxiliarContable.Domain.Shared/Emails</c>
/// como recursos incrustados.
///
/// <para>El layout sustituye al de ABP (<see cref="StandardEmailTemplates.Layout"/>), así que
/// CUALQUIER correo de ABP sale con la marca, no solo los propios. El layout solo recibe el
/// contexto global, no el modelo (verificado en <c>refs/abp</c>,
/// <c>ScribanTemplateRenderingEngine.RenderInternalAsync</c>): lo que necesita —logo, url de
/// la consola— se lo pasa <c>PlantillasDeCorreo</c> en el host, y sin eso cae en texto.</para>
/// </summary>
public static class AuxiliarEmailTemplates
{
    public const string Invitacion = "AuxiliarContable.Invitacion";

    /// <summary>Un código de un solo uso: verificar correo o entrar con verificación en dos pasos.</summary>
    public const string Codigo = "AuxiliarContable.Codigo";

    /// <summary>Lo que el cliente dijo que subiría después y no ha subido.</summary>
    public const string Recordatorio = "AuxiliarContable.Recordatorio";

    /// <summary>
    /// El de «olvidé mi contraseña» de ABP (<c>AccountEmailTemplates.PasswordResetLink</c>).
    /// Va como texto porque su clase vive en Account.Application, que el dominio no referencia.
    /// </summary>
    public const string RestablecerContrasena = "Abp.Account.PasswordResetLink";
}

public class AuxiliarEmailTemplateDefinitionProvider : TemplateDefinitionProvider
{
    public override void Define(ITemplateDefinitionContext context)
    {
        context.Add(
            new TemplateDefinition(AuxiliarEmailTemplates.Invitacion, layout: StandardEmailTemplates.Layout)
                .WithVirtualFilePath("/Emails/Invitacion.tpl", isInlineLocalized: true));

        context.Add(
            new TemplateDefinition(AuxiliarEmailTemplates.Codigo, layout: StandardEmailTemplates.Layout)
                .WithVirtualFilePath("/Emails/Codigo.tpl", isInlineLocalized: true));

        context.Add(
            new TemplateDefinition(AuxiliarEmailTemplates.Recordatorio, layout: StandardEmailTemplates.Layout)
                .WithVirtualFilePath("/Emails/Recordatorio.tpl", isInlineLocalized: true));
    }

    /// <summary>
    /// En PostDefine y no en Define: aquí ya definieron las suyas todos los módulos, así que
    /// lo que se cambia existe sea cual sea el orden de carga.
    /// </summary>
    public override void PostDefine(ITemplateDefinitionContext context)
    {
        context.GetOrNull(StandardEmailTemplates.Layout)
            ?.WithVirtualFilePath("/Emails/Layout.tpl", isInlineLocalized: true);

        context.GetOrNull(AuxiliarEmailTemplates.RestablecerContrasena)
            ?.WithVirtualFilePath("/Emails/RestablecerContrasena.tpl", isInlineLocalized: true);
    }
}
