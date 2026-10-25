using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IgasConsole.Acceso;

/// <summary>
/// Un POST al formulario de login de alguien que YA tiene sesión no vuelve a iniciar sesión: se
/// le lleva a donde iba (su <c>ReturnUrl</c>, normalmente <c>/connect/authorize…</c>).
///
/// <para>⚠️ Rompió producción: dos toques seguidos en «Entrar» mandaron dos POST. El primero
/// inició sesión; el segundo llegó con el token antifalsificación emitido cuando aún no había
/// sesión → <c>AntiforgeryValidationException</c> («meant for a different claims-based user»)
/// → error 400, y desde esa página la persona acabó en la raíz de la API en vez de en la
/// consola. El script del formulario frena el doble toque, pero con latencia el primer POST
/// puede volver antes del segundo toque: por eso se ataja también aquí.</para>
///
/// <para>Seguro: no cambia nada (no hay sesión que crear), y solo redirige a rutas LOCALES
/// —el <c>ReturnUrl</c> de un formulario de login es un parámetro que manda el cliente, y
/// aceptar uno externo sería una redirección abierta—.</para>
///
/// <para>Va DESPUÉS de autenticar y de validar la sesión (si la sesión ya no vale, la petición
/// sigue como anónima y el login hace lo suyo).</para>
/// </summary>
public class LoginYaIniciadoMiddleware
{
    private readonly RequestDelegate _siguiente;

    public LoginYaIniciadoMiddleware(RequestDelegate siguiente)
    {
        _siguiente = siguiente;
    }

    public async Task InvokeAsync(HttpContext contexto, IOptions<AccesoOptions> acceso)
    {
        if (HttpMethods.IsPost(contexto.Request.Method)
            && contexto.User.Identity?.IsAuthenticated == true
            && EsElLogin(contexto.Request.Path, acceso.Value))
        {
            var destino = contexto.Request.Query["ReturnUrl"].ToString();
            contexto.Response.Redirect(EsLocal(destino) ? destino : "/");
            return;
        }

        await _siguiente(contexto);
    }

    private static bool EsElLogin(PathString ruta, AccesoOptions acceso)
    {
        var login = acceso.EstaOculto ? "/" + acceso.SegmentoNormalizado : "/Account/Login";
        return string.Equals(ruta.Value?.TrimEnd('/'), login, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ruta del mismo sitio: empieza por «/» pero no por «//» ni «/\» (otro host).</summary>
    internal static bool EsLocal(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
