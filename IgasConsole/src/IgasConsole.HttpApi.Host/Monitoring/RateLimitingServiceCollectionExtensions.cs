using System.Globalization;
using System.Threading.RateLimiting;
using IgasConsole.Acceso;
using IgasConsole.Monitoring;
using IgasConsole.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IgasConsole.HealthChecks;

public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Rutas por donde se envian credenciales o se obtienen tokens. Se comparan con
    /// StartsWithSegments (no Contains) para que solo cuente el segmento de ruta
    /// completo: "/Account/Loginzzz" no es "/Account/Login".
    ///
    /// <para>⚠️ <b>El segmento del host entra aqui.</b> Cuando el acceso esta oculto
    /// (<see cref="AccesoConsts"/>) el formulario de login deja de estar en
    /// <c>/Account/Login</c> y pasa a <c>/{segmento}</c>. Sin esta linea, mover el login le
    /// quitaria su limite estricto —10 intentos por minuto frente a 120— y el formulario de
    /// contraseñas se quedaria con la cuota de navegacion normal. No fallaria nada visible:
    /// simplemente se podrian probar 120 contraseñas por minuto en vez de 10, que es lo
    /// contrario de lo que persigue esconder la ruta.</para>
    ///
    /// <para>La ruta vieja se deja en la lista aunque devuelva 404 al estar oculta: no cuesta
    /// nada y si el segmento se retira, el limite vuelve a aplicar sin tocar esto.</para>
    /// </summary>
    private static bool EsRutaDeCredenciales(PathString ruta, string segmentoDelHost)
    {
        if (segmentoDelHost.Length > 0 &&
            ruta.StartsWithSegments("/" + segmentoDelHost, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ruta.StartsWithSegments("/Account/Login", System.StringComparison.OrdinalIgnoreCase)
            || ruta.StartsWithSegments("/Account/ForgotPassword", System.StringComparison.OrdinalIgnoreCase)
            || ruta.StartsWithSegments("/Account/ResetPassword", System.StringComparison.OrdinalIgnoreCase)
            // El segundo paso del login: cada visita puede mandar un correo o un SMS.
            || ruta.StartsWithSegments("/Account/TwoFactor", System.StringComparison.OrdinalIgnoreCase)
            // Lo mismo por la API: cada llamada puede mandar un correo de verdad.
            || ruta.StartsWithSegments("/api/account/send-password-reset-code", System.StringComparison.OrdinalIgnoreCase)
            || ruta.StartsWithSegments("/api/account/reset-password", System.StringComparison.OrdinalIgnoreCase)
            // Registro por invitacion: anonimo, y cada llamada prueba un token.
            || ruta.StartsWithSegments("/api/app/registro", System.StringComparison.OrdinalIgnoreCase)
            // Activar un nodo y pedir el instalador: anonimos, y cada llamada prueba una clave de licencia.
            || ruta.StartsWithSegments("/api/nodo/activar", System.StringComparison.OrdinalIgnoreCase)
            || ruta.StartsWithSegments("/api/nodo/instalador", System.StringComparison.OrdinalIgnoreCase)
            // Verificar correo y teléfono: SOLO lo que manda o prueba un código (un SMS
            // cuesta). ⚠️ No el prefijo entero: la consulta del estado la hace el aviso de
            // «Protege tu cuenta» en cada carga de la app, y con 10/min acababa en 429 (lo
            // cazaron las pruebas de e2e/).
            || ruta.StartsWithSegments("/api/app/seguridad-de-cuenta/enviar-codigo-correo", System.StringComparison.OrdinalIgnoreCase)
            || ruta.StartsWithSegments("/api/app/seguridad-de-cuenta/enviar-codigo-telefono", System.StringComparison.OrdinalIgnoreCase)
            || ruta.StartsWithSegments("/api/app/seguridad-de-cuenta/confirmar-correo", System.StringComparison.OrdinalIgnoreCase)
            || ruta.StartsWithSegments("/api/app/seguridad-de-cuenta/confirmar-telefono", System.StringComparison.OrdinalIgnoreCase);
        // ⚠️ /connect/token NO va aquí: tiene partición y límite propios (ver
        // RateLimitingOptions.TokenPermitLimit). Con los 10/min del login, recargar la
        // página unas cuantas veces cerraba la sesión.
    }

    /// <summary>
    /// Limita peticiones por IP. Particionado (no una sola ventana global) porque un
    /// límite compartido entre todos los clientes dejaría que un solo abusador se comiera
    /// la cuota de todo el mundo.
    /// </summary>
    public static void AddIgasConsoleRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var opciones = httpContext.RequestServices
                    .GetRequiredService<IOptions<RateLimitingOptions>>().Value;

                // El propio endpoint de salud lo consulta el recolector cada minuto y el
                // bucle de despliegue: contarlo como tráfico de cliente lo bloquearía a
                // él mismo, mismo criterio que RequestMetricsMiddleware.
                if (!opciones.IsEnabled ||
                    httpContext.Request.Path.StartsWithSegments("/health-status"))
                {
                    return RateLimitPartition.GetNoLimiter("exento");
                }

                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida";

                // Las rutas donde se prueban contraseñas llevan un limite MUCHO mas
                // estricto. 120/min esta bien para navegar la API, pero son 120 intentos
                // de contraseña por minuto contra el formulario de login.
                //
                // Es /Account/Login y no /connect/token: el cliente Angular solo tiene los
                // grants authorization_code y refresh_token (se le quito 'password'), asi
                // que por el endpoint de token ya no se pueden enviar credenciales. El
                // formulario Razor del host es la superficie que queda.
                //
                // El bloqueo de cuenta de Identity (5 intentos) protege UNA cuenta ya
                // conocida; esto frena el barrido de contraseñas comunes contra muchos
                // usuarios distintos, que el bloqueo por cuenta no ve.
                var acceso = httpContext.RequestServices
                    .GetRequiredService<IOptions<AccesoOptions>>().Value;

                if (EsRutaDeCredenciales(httpContext.Request.Path, acceso.SegmentoNormalizado))
                {
                    // Particion aparte ("auth:"): si compartiera clave con la global, el
                    // trafico normal de la API gastaria la cuota del login y al reves.
                    return RateLimitPartition.GetFixedWindowLimiter(
                        "auth:" + ip, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = opciones.LoginPermitLimit,
                            Window = System.TimeSpan.FromSeconds(opciones.WindowSeconds),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                }

                if (httpContext.Request.Path.StartsWithSegments("/connect/token", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Partición propia ("token:"): ni gasta la cuota del login ni la de la API.
                    return RateLimitPartition.GetFixedWindowLimiter(
                        "token:" + ip, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = opciones.TokenPermitLimit,
                            Window = System.TimeSpan.FromSeconds(opciones.WindowSeconds),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                }

                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = opciones.PermitLimit,
                    Window = System.TimeSpan.FromSeconds(opciones.WindowSeconds),
                    QueueLimit = opciones.QueueLimit,
                    AutoReplenishment = true
                });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var acumulador = context.HttpContext.RequestServices
                    .GetRequiredService<IRateLimitAccumulator>();

                var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida";

                // El patrón de ruta ("/api/app/product/{id}"), no la URL con el id real:
                // si no, cada petición distinta generaría una fila distinta en el panel.
                // Solo está resuelto porque UseRateLimiter va DESPUÉS de UseRouting.
                var endpoint = (context.HttpContext.GetEndpoint() as RouteEndpoint)
                    ?.RoutePattern.RawText
                    ?? context.HttpContext.Request.Path.Value
                    ?? "/";

                if (endpoint.Length > MonitoringConsts.MaxEndpointLength)
                {
                    endpoint = endpoint[..MonitoringConsts.MaxEndpointLength];
                }

                acumulador.Record(ip, endpoint);

                var respuesta = context.HttpContext.Response;
                respuesta.StatusCode = StatusCodes.Status429TooManyRequests;

                // Cuanto falta para que la ventana se renueve, si el limitador lo sabe.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    respuesta.Headers.RetryAfter =
                        ((int)espera.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                // ESCRIBIR EL CUERPO NO ES COSMETICO. Fuera de Development el host monta
                // UseErrorPage(), que es UseStatusCodePagesWithRedirects: convierte
                // cualquier 4xx/5xx SIN cuerpo en un 302 a /Error?httpStatusCode=429.
                // Un cliente de API recibia un redirect en vez del 429, y el access.log
                // de nginx registraba 302, con lo que la jail de fail2ban que busca 429
                // no veia nada. StatusCodePages se abstiene si la respuesta ya trae
                // ContentType y cuerpo, asi que esto es lo que mantiene el 429 intacto.
                respuesta.ContentType = "application/json; charset=utf-8";
                await respuesta.WriteAsync(
                    "{\"error\":{\"code\":\"TooManyRequests\",\"message\":" +
                    "\"Demasiadas peticiones. Espera un momento y vuelve a intentarlo.\"}}",
                    cancellationToken);
            };
        });
    }
}
