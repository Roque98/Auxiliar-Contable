using System.Threading.Tasks;
using IgasConsole.Branding;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Volo.Abp.AspNetCore.Mvc;
using IgasConsole.Notifications.Brevo;
using Microsoft.Extensions.Options;
using Volo.Abp.MultiTenancy;

namespace IgasConsole.HttpApi.Host.Branding;

/// <summary>
/// Mete el color de acento del inquilino en el <c>&lt;head&gt;</c> de las páginas MVC.
///
/// <para>Va enganchado al hook <c>LayoutHooks.Head.Last</c> (ver el módulo del Host), que
/// el tema LeptonX Lite renderiza —comprobado en el ensamblado del tema, que invoca
/// <c>Header.Last</c>—. Es el último sitio del <c>head</c>, así que estas reglas ganan a
/// las del tema sin necesidad de <c>!important</c>.</para>
///
/// <para>⚠️ <b>Se redefinen CUATRO variables, porque los dos lados de la aplicación no usan
/// la misma.</b> En el Angular el acento es <c>--lpx-brand</c> (14 usos en sus hojas;
/// <c>--lpx-primary</c> allí no lo consume nadie). En las páginas MVC es justo al revés:
/// manda <c>--lpx-primary</c>, que alimenta a <c>--bs-primary</c> de Bootstrap. Se descubrió
/// midiendo <c>getComputedStyle</c> en la página de login: con solo <c>--lpx-brand</c>, la
/// variable cambiaba de valor y los botones seguían exactamente del color de siempre.</para>
///
/// <para><c>--bs-primary-rgb</c> va aparte porque Bootstrap la usa para las variantes con
/// transparencia (<c>rgba(var(--bs-primary-rgb), .25)</c>, el halo de foco de los campos);
/// sin ella, esos detalles se quedarían del color anterior.</para>
///
/// <para>⚠️ Aquí se escribe HTML SIN ESCAPAR, que es exactamente donde una inyección de CSS
/// entraría. Lo que lo hace seguro no es esta clase sino <see cref="ColorDeMarca"/>: el
/// valor ya ha pasado por una lista blanca de forma exacta (<c>#rrggbb</c>) y no puede
/// contener ni llaves ni punto y coma ni <c>&lt;</c>. Si algún día se admite otro formato
/// de color, este archivo es el que se vuelve peligroso.</para>
/// </summary>
public class BrandingStyleViewComponent : AbpViewComponent
{
    private readonly LectorDeMarca _lector;
    private readonly ICurrentTenant _inquilinoActual;
    private readonly BrevoOptions _brevo;

    public BrandingStyleViewComponent(LectorDeMarca lector, ICurrentTenant inquilinoActual, IOptions<BrevoOptions> brevo)
    {
        _lector = lector;
        _inquilinoActual = inquilinoActual;
        _brevo = brevo.Value;
    }

    /// <summary>
    /// El acento de la marca como tokens del design system (<c>--ig-acento</c> y el texto que
    /// va encima), para las páginas del servidor: login, recuperar contraseña, segundo factor.
    /// El resto de su aspecto está en <c>wwwroot/global-styles.css</c>.
    ///
    /// <para>⚠️ «¿Olvidaste tu contraseña?» solo aparece si Brevo puede mandar el correo (API key
    /// Y remitente). Sin eso la página diría «te lo enviamos» y no llegaría nada: quien lo
    /// pidiera se quedaría esperando en vez de buscar otra vía.</para>
    /// </summary>
    public virtual async Task<IViewComponentResult> InvokeAsync()
    {
        var marca = await _lector.LeerAsync();
        var acento = marca.AccentColor;

        var css =
            ":root{" +
            $"--ig-acento:{acento};" +
            $"--ig-sobre-acento:{ColorDeMarca.TextoSobre(acento)};" +
            $"--lpx-brand:{acento};" +
            $"--lpx-primary:{acento};" +
            $"--bs-primary:{acento};" +
            $"--bs-primary-rgb:{ColorDeMarca.ARgb(acento)};" +
            "}" +
            (PuedeMandarCorreo() ? string.Empty : OcultarRecuperacion) +
            (_inquilinoActual.Id.HasValue ? OcultarCambioDeInquilino : string.Empty);

        return new HtmlContentViewComponentResult(new HtmlString($"<style>{css}</style>"));
    }

    private bool PuedeMandarCorreo()
    {
        return _brevo.TieneCredenciales && !string.IsNullOrWhiteSpace(_brevo.SenderEmail);
    }

    /// <summary>Los dos caminos: con el acceso oculto la página vive en <c>/{segmento}/recuperar</c>.</summary>
    private const string OcultarRecuperacion =
        ".abp-account-layout a[href*=\"/Account/ForgotPassword\"],.abp-account-layout a[href*=\"/recuperar\"]{display:none !important;}";

    private const string OcultarCambioDeInquilino = "#AbpTenantSwitchLink{display:none !important;}";
}
