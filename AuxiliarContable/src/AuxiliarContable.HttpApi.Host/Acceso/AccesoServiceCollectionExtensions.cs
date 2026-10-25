using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuxiliarContable.Acceso;

public static class AccesoServiceCollectionExtensions
{
    /// <summary>
    /// Rutas por las que SÍ se puede llegar al formulario de login. Es una sola: la petición
    /// de autorización de OpenIddict, que es como llega cualquiera que pulse «entrar» en el
    /// SPA.
    /// </summary>
    private const string FlujoDeAutorizacion = "/connect/authorize";

    /// <summary>
    /// Aplica el ocultamiento del acceso del host. Si no hay segmento configurado no hace
    /// <b>nada</b> y todo se comporta como en una instalación de ABP recién creada.
    /// </summary>
    public static void AddAuxiliarContableAcceso(this IServiceCollection services, IConfiguration configuration)
    {
        var segmento = AccesoConsts.Normalizar(configuration[AccesoConsts.ClaveSegmentoDelHost]);

        services.Configure<AccesoOptions>(options => options.SegmentoDelHost = segmento);

        if (segmento.Length == 0)
        {
            return;
        }

        services.Configure<RazorPagesOptions>(options =>
        {
            options.Conventions.Add(new RutaDeLoginConvention(segmento));
        });

        services.ConfigureApplicationCookie(options =>
        {
            // A donde manda ASP.NET a quien no ha iniciado sesión. Sin esto, el formulario
            // estaría en la ruta nueva pero el servidor seguiría mandando a la vieja, que ya
            // no existe: el login acabaría en un 404 en vez de en la pantalla.
            options.LoginPath = "/" + segmento;

            var redirigirComoSiempre = options.Events.OnRedirectToLogin;

            // ⚠️⚠️ SIN ESTO EL SEGMENTO SE REGALA. La redirección al login lleva la ruta
            // nueva en la cabecera `Location`, así que CUALQUIER página MVC protegida
            // —`/Account/Manage` es la obvia— se convierte en un oráculo: se pide sin sesión,
            // se lee el `Location` y ahí está el segmento, sin haber adivinado nada.
            //
            // Se responde 404 salvo que la petición venga del flujo de autorización, que es
            // el único camino legítimo hasta el formulario. Quien pulsa «entrar» en el SPA
            // pasa por ahí y no nota nada.
            //
            // No es hermético y no pretende serlo: quien construya a mano un
            // /connect/authorize válido —los parámetros están en el JavaScript del SPA, que
            // es público— recibe la redirección y con ella el segmento. Cierra el camino
            // barato, que es el que usan los escáneres.
            options.Events.OnRedirectToLogin = contexto =>
            {
                if (contexto.Request.Path.StartsWithSegments(FlujoDeAutorizacion))
                {
                    return redirigirComoSiempre(contexto);
                }

                return NoHayNadaAqui(contexto.Response);
            };
        });
    }

    /// <summary>
    /// ⚠️ <b>Escribe cuerpo y ContentType.</b> Fuera de Development el host monta
    /// <c>UseErrorPage()</c>, que convierte cualquier 4xx <b>sin cuerpo</b> en un 302 a la
    /// página de error. Un 404 pelado aquí se vería como un redirect — y un redirect es
    /// justamente la señal que este método existe para no dar. Es la cuarta vez que muerde
    /// esa trampa en el proyecto (ver CLAUDE.md §5).
    /// </summary>
    private static Task NoHayNadaAqui(HttpResponse respuesta)
    {
        respuesta.StatusCode = StatusCodes.Status404NotFound;
        respuesta.ContentType = "text/plain; charset=utf-8";
        return respuesta.WriteAsync("Not found.");
    }
}
