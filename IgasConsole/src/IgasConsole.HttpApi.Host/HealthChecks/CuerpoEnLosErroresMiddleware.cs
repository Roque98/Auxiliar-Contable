using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Le da cuerpo a las respuestas de error que no lo traen, para que
/// <c>UseErrorPage()</c> no las convierta en un redirect.
///
/// <para><b>El problema, de raíz.</b> Fuera de Development el host llama a
/// <c>app.UseErrorPage()</c>, que es
/// <c>UseStatusCodePagesWithRedirects("~/Error?httpStatusCode={0}")</c>: envuelve todo lo que
/// se registre después y transforma cualquier respuesta 400-599 <b>sin cuerpo</b> en un
/// <b>302</b>. El proyecto ya había tropezado con esto cuatro veces —el 429 del rate limiting,
/// el 403 del bloqueo de IPs, el 401 de las claves de API y el 404 del logo— y cada vez se
/// arregló <b>en el sitio que escribía la respuesta</b>. Eso deja fuera todo lo que no
/// escribimos nosotros.</para>
///
/// <para>Y eso es la mayoría: una ruta que no existe, un método no permitido, un
/// <c>Content-Type</c> que no se acepta. Comprobado en producción — <c>/Account/Login</c>,
/// <c>/api/app/no-existe</c> y cualquier URL inventada respondían <c>302</c> a
/// <c>/Error?httpStatusCode=404</c>. Un cliente de API que pide una ruta equivocada recibía un
/// redirect a una página HTML en vez de un 404 que pueda interpretar.</para>
///
/// <para>⚠️ <b>El detector de escaneo NO estaba afectado</b>, y conviene saberlo por si alguien
/// se plantea quitar esto: <c>AspNetCoreAuditLogContributor</c> lee <c>Response.StatusCode</c>
/// dentro del ámbito de auditoría, que va <b>por dentro</b> de <c>UseErrorPage</c>. En
/// <c>AbpAuditLogs</c> queda grabado el <b>404</b>, no el 302, así que el bloqueo automático
/// sigue contando esas peticiones como fallos (§6.15).</para>
///
/// <para><b>Se respeta la página de error para quien la puede leer.</b> Si el cliente pide
/// HTML —o sea, una persona navegando a una pantalla MVC— se deja pasar y
/// <c>UseErrorPage</c> hace lo suyo, que ahí es lo correcto. Lo que se corrige es el caso del
/// cliente que no entiende una página: para él, un código de estado es la respuesta.</para>
///
/// <para>⚠️ <b>Va registrado JUSTO DESPUÉS de <c>UseErrorPage()</c></b>, no antes. Tiene que
/// correr <b>por dentro</b> para poder tocar la respuesta mientras vuelve, antes de que
/// <c>StatusCodePages</c> la mire. Registrado por fuera no serviría de nada y no fallaría
/// nada — es la clase de error que solo se ve en producción.</para>
/// </summary>
public class CuerpoEnLosErroresMiddleware
{
    private readonly RequestDelegate _siguiente;

    public CuerpoEnLosErroresMiddleware(RequestDelegate siguiente)
    {
        _siguiente = siguiente;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _siguiente(context);

        if (!HayQueRellenar(context))
        {
            return;
        }

        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = new
            {
                code = context.Response.StatusCode.ToString(),
                message = ReasonPhrases(context.Response.StatusCode)
            }
        }));
    }

    private static bool HayQueRellenar(HttpContext context)
    {
        var respuesta = context.Response;

        // Ya se empezó a escribir: no se puede tocar, y además StatusCodePages tampoco
        // actuaría.
        if (respuesta.HasStarted)
        {
            return false;
        }

        if (respuesta.StatusCode < 400 || respuesta.StatusCode >= 600)
        {
            return false;
        }

        // Ya trae cuerpo o tipo: quien la escribió ya se ocupó, y StatusCodePages se abstiene
        // por su cuenta. Es el caso de los cuatro 4xx que el proyecto ya escribía a mano.
        if (respuesta.ContentLength.HasValue || !string.IsNullOrEmpty(respuesta.ContentType))
        {
            return false;
        }

        // Una persona con un navegador sí puede leer la página de error de ABP, y para ella
        // es mejor que un JSON. El navegador lo pide explícitamente en Accept.
        return !QuiereHtml(context.Request);
    }

    private static bool QuiereHtml(HttpRequest peticion)
    {
        foreach (var valor in peticion.Headers.Accept)
        {
            if (valor != null && valor.Contains("text/html", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Texto corto y genérico. <b>No</b> se detalla el motivo: estas respuestas se dan a
    /// cualquiera sin autenticar, y decir de más aquí es lo que convierte un 404 en un oráculo
    /// (§6.98).
    /// </summary>
    private static string ReasonPhrases(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad request.",
        StatusCodes.Status401Unauthorized => "Unauthorized.",
        StatusCodes.Status403Forbidden => "Forbidden.",
        StatusCodes.Status404NotFound => "Not found.",
        StatusCodes.Status405MethodNotAllowed => "Method not allowed.",
        _ => statusCode >= 500 ? "Server error." : "Request error."
    };
}
