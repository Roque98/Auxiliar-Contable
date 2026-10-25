using Volo.Abp.Emailing.Templates;
using Volo.Abp.TextTemplating;

namespace IgasConsole.Emails;

/// <summary>
/// Los correos de ZIA. Las plantillas (Scriban) están en <c>IgasConsole.Domain.Shared/Emails</c>
/// como recursos incrustados.
///
/// <para>El layout sustituye al de ABP (<see cref="StandardEmailTemplates.Layout"/>), así que
/// CUALQUIER correo de ABP sale con la marca, no solo los propios. El layout solo recibe el
/// contexto global, no el modelo (verificado en <c>refs/abp</c>,
/// <c>ScribanTemplateRenderingEngine.RenderInternalAsync</c>): lo que necesita —logo, url de
/// la consola— se lo pasa <c>PlantillasDeCorreo</c> en el host, y sin eso cae en texto.</para>
/// </summary>
public static class IgasEmailTemplates
{
    public const string Invitacion = "IgasConsole.Invitacion";

    /// <summary>Un código de un solo uso: verificar correo o entrar con verificación en dos pasos.</summary>
    public const string Codigo = "IgasConsole.Codigo";

    /// <summary>
    /// El de «olvidé mi contraseña» de ABP (<c>AccountEmailTemplates.PasswordResetLink</c>).
    /// Va como texto porque su clase vive en Account.Application, que el dominio no referencia.
    /// </summary>
    public const string RestablecerContrasena = "Abp.Account.PasswordResetLink";
}

public class IgasEmailTemplateDefinitionProvider : TemplateDefinitionProvider
{
    public override void Define(ITemplateDefinitionContext context)
    {
        context.Add(
            new TemplateDefinition(IgasEmailTemplates.Invitacion, layout: StandardEmailTemplates.Layout)
                .WithVirtualFilePath("/Emails/Invitacion.tpl", isInlineLocalized: true));

        context.Add(
            new TemplateDefinition(IgasEmailTemplates.Codigo, layout: StandardEmailTemplates.Layout)
                .WithVirtualFilePath("/Emails/Codigo.tpl", isInlineLocalized: true));
    }

    /// <summary>
    /// En PostDefine y no en Define: aquí ya definieron las suyas todos los módulos, así que
    /// lo que se cambia existe sea cual sea el orden de carga.
    /// </summary>
    public override void PostDefine(ITemplateDefinitionContext context)
    {
        context.GetOrNull(StandardEmailTemplates.Layout)
            ?.WithVirtualFilePath("/Emails/Layout.tpl", isInlineLocalized: true);

        context.GetOrNull(IgasEmailTemplates.RestablecerContrasena)
            ?.WithVirtualFilePath("/Emails/RestablecerContrasena.tpl", isInlineLocalized: true);
    }
}
