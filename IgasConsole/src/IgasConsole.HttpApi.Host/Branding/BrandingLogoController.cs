using System.Threading.Tasks;
using IgasConsole.Branding;
using IgasConsole.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.BlobStoring;

namespace IgasConsole.HttpApi.Host.Branding;

/// <summary>
/// Sirve el logo del inquilino. Es el único endpoint <b>anónimo</b> de la marca, y tiene
/// que serlo: la primera pantalla que lo enseña es la de login, donde por definición
/// todavía no hay sesión ni token, y la etiqueta que lo pide es un
/// <c>background-image</c> — que no manda cabecera <c>Authorization</c> ni podría.
///
/// <para>Vive en el Host y no en <c>HttpApi</c> por dos razones que van juntas: necesita el
/// contenedor de BLOBs (capa de dominio, que <c>HttpApi</c> no referencia a propósito) y
/// necesita fijar a mano las cabeceras de caché y de tipo de contenido, que son decisiones
/// del borde HTTP. Un application service auto-mapeado no puede hacer ni lo uno ni lo
/// otro.</para>
///
/// <para>Qué se expone al hacerlo anónimo: el logo de un inquilino a quien conozca su id.
/// No es información nueva — el selector de inquilinos de la propia pantalla de login ya
/// permite comprobar cuáles existen— y un logo es, por definición, lo que esa organización
/// enseña en público.</para>
/// </summary>
[AllowAnonymous]
[Route("api/branding")]
[ApiExplorerSettings(IgnoreApi = true)]
public class BrandingLogoController : AbpControllerBase
{
    private readonly IBlobContainer<TenantLogoContainer> _logos;

    public BrandingLogoController(IBlobContainer<TenantLogoContainer> logos)
    {
        _logos = logos;
    }

    /// <summary>
    /// <c>GET /api/branding/logo?__tenant={id}&amp;v={version}</c>
    ///
    /// <para>El inquilino NO se lee aquí: lo resuelve el middleware de multi-tenancy de ABP
    /// a partir de <c>__tenant</c> —<c>QueryStringTenantResolveContributor</c> va registrado
    /// de serie— y el contenedor de BLOBs ya guarda y busca por <c>ICurrentTenant.Id</c>.
    /// Leerlo a mano sería una segunda resolución que puede discrepar de la de verdad.</para>
    /// </summary>
    [HttpGet("logo")]
    public async Task<IActionResult> GetLogoAsync([FromQuery] string? v = null)
    {
        var bytes = await _logos.GetAllBytesOrNullAsync(BrandingConsts.LogoBlobName);

        if (bytes == null || bytes.Length == 0)
        {
            return SinLogo();
        }

        // El tipo sale de los BYTES, nunca de lo que declaró quien subió el archivo. Es la
        // misma regla que en la foto de perfil y aquí pesa más, porque esto se sirve sin
        // autenticar: devolver un text/html que alguien subió sería XSS almacenado servido
        // a cualquiera. Si no se reconoce, no se sirve.
        var contentType = DetectorDeImagen.ContentType(DetectorDeImagen.Detectar(bytes));
        if (contentType == null)
        {
            return SinLogo();
        }

        // Y por si acaso: nosniff impide que el navegador ignore el tipo declarado y decida
        // por su cuenta que esto era HTML.
        Response.Headers.XContentTypeOptions = "nosniff";

        if (!string.IsNullOrWhiteSpace(v))
        {
            // Cacheable para siempre PORQUE la URL lleva la versión: un logo nuevo cambia la
            // versión, con ella la URL, y el navegador pide otra cosa. Sin esto, la pantalla
            // de login volvería a bajar el logo en cada visita.
            //
            // "public" es seguro aquí porque el inquilino también va en la URL (ver
            // LectorDeMarca): dos inquilinos nunca comparten dirección, así que una caché
            // intermedia no puede servirle a uno el logo del otro.
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
        else
        {
            // Sin versión no hay nada que prometa que esto siga siendo válido mañana.
            Response.Headers.CacheControl = "no-store";
        }

        return File(bytes, contentType);
    }

    /// <summary>
    /// El 404 de «este inquilino no tiene logo».
    ///
    /// <para>⚠️⚠️ <b>Lleva cuerpo y <c>ContentType</c> A PROPÓSITO</b>, y por eso no es un
    /// <c>NotFound()</c> a secas. Fuera de Development el Host llama a
    /// <c>UseErrorPage()</c>, que es <c>UseStatusCodePagesWithRedirects</c>: convierte
    /// cualquier respuesta 400-599 <b>sin cuerpo</b> en un <b>302</b> a la página de error.
    /// <c>StatusCodePages</c> se abstiene si la respuesta ya trae cuerpo. Es la misma trampa
    /// que ya mordió al 429 del rate limiting y al 403 del bloqueo de IPs (ver CLAUDE.md
    /// §5).</para>
    ///
    /// <para>Pasó de verdad aquí: en local daba 404 y en producción devolvía <c>302</c>. Un
    /// <c>background-image</c> que pide un logo inexistente acababa siguiendo el redirect y
    /// bajándose la página de error entera —HTML— para no pintar nada; y una caché por el
    /// camino puede guardar ese redirect. Solo se vio comprobando contra producción: en
    /// Development este middleware ni existe.</para>
    ///
    /// <para>El cuerpo es texto plano y deliberadamente escueto: quien pide esto es una
    /// etiqueta de imagen, no una persona, así que un JSON de error no lo lee nadie.</para>
    /// </summary>
    private IActionResult SinLogo()
    {
        // no-store: que no haya logo hoy no dice nada de mañana — es justo lo que cambia en
        // cuanto alguien sube uno.
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";

        // El código va explícito: Content(...) responde 200, así que devolverlo tal cual
        // convertiría «no hay logo» en «aquí tienes un logo que resulta ser texto».
        return new ContentResult
        {
            StatusCode = StatusCodes.Status404NotFound,
            ContentType = "text/plain; charset=utf-8",
            Content = "Sin logo."
        };
    }
}
