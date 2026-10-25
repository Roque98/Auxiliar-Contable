using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace IgasConsole.Acceso;

/// <summary>
/// Mueve las páginas de cuenta del módulo Account de <c>/Account/...</c> al segmento
/// configurado. Ver <see cref="AccesoConsts"/>.
///
/// <para>Se cambia la <b>ruta</b> de la página, no la página: el <c>.cshtml</c> sigue siendo el
/// del módulo y se actualiza con él, igual que ya se hace con
/// <c>IgasConsoleLoginModel</c>, que sustituye solo el modelo. Renombrar la ruta es lo único que
/// hace falta, porque el formulario se envía a sí mismo (<c>&lt;form method="post"&gt;</c> sin
/// action, verificado en <c>refs/abp</c>) y los enlaces internos usan <c>asp-page</c> o
/// <c>Url.Page</c>, que resuelven por el <b>nombre</b> de la página y no por su URL. O sea que
/// todo lo de dentro sigue el cambio solo.</para>
///
/// <para>⚠️ Lo que <b>no</b> sigue el cambio solo, y por eso hay que tocarlo aparte:</para>
/// <list type="bullet">
///   <item><description><c>LoginPath</c> de la cookie de Identity, a donde ASP.NET manda a
///   quien no ha iniciado sesión. Se ajusta en <c>AccesoServiceCollectionExtensions</c>.</description></item>
///   <item><description>El límite de intentos del rate limiter, que tenía
///   <c>/Account/Login</c> escrito a mano. Sin ajustarlo, mover el login le quitaría al
///   formulario de contraseñas su límite estricto — exactamente lo contrario de lo que se
///   busca aquí.</description></item>
/// </list>
///
/// <para>Al mover la ruta, <c>/Account/Login</c> deja de existir y responde 404. Eso es el
/// objetivo: es la cadena exacta que busca un escáner para saber que enfrente hay un ABP.</para>
///
/// <para>⚠️⚠️ <b>El login no era la única puerta, y las otras se quedaron fuera la primera
/// vez.</b> Se vio <b>mirando la pantalla de login desplegada</b>: enseña «Register» y
/// «Forgot password?», y las dos páginas seguían respondiendo <b>200 en su dirección por
/// defecto</b> mientras el login ya estaba escondido. Esconder una de tres puertas no esconde
/// nada — `/Account/Register` identifica un ABP igual de bien que `/Account/Login`.</para>
/// </summary>
public class RutaDeLoginConvention : IPageRouteModelConvention
{
    /// <summary>
    /// Rutas de las páginas dentro del módulo, tal cual las nombra Razor Pages. No son URLs:
    /// son la posición de cada <c>.cshtml</c> en el árbol de páginas, y no cambian al mover la
    /// ruta. A la derecha, el sufijo que le toca dentro del segmento.
    ///
    /// <para>El login se queda con el segmento pelado —es la puerta principal— y las otras dos
    /// cuelgan de él.</para>
    /// </summary>
    private static readonly Dictionary<string, string> Paginas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/Account/Login"] = "",
        ["/Account/Register"] = "/registro",
        ["/Account/ForgotPassword"] = "/recuperar",
    };

    private readonly string _segmento;

    public RutaDeLoginConvention(string segmento)
    {
        _segmento = segmento;
    }

    public void Apply(PageRouteModel model)
    {
        if (model.ViewEnginePath == null ||
            !Paginas.TryGetValue(model.ViewEnginePath, out var sufijo))
        {
            return;
        }

        foreach (var selector in model.Selectors)
        {
            if (selector.AttributeRouteModel != null)
            {
                selector.AttributeRouteModel.Template = _segmento + sufijo;
            }
        }
    }
}
