using System;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using IgasConsole.ApiKeys;
using IgasConsole.Monitoring;
using IgasConsole.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Security.Claims;

namespace IgasConsole.RateLimiting;

/// <summary>
/// Límite de peticiones POR IDENTIDAD: por usuario autenticado y por clave de API.
///
/// <para>El límite por IP que ya existe no cubre este caso. Un script propio con un bucle
/// roto sale de una dirección legítima —a menudo la misma que usan compañeros— y bajar el
/// límite de IP para atraparlo dejaría fuera a los demás. Esto acota por credencial, que es
/// lo que de verdad se ha desbocado.</para>
///
/// <para>⚠️ POR QUÉ ES UN MIDDLEWARE PROPIO Y NO OTRO <c>UseRateLimiter()</c>:
/// <c>app.UseRateLimiter()</c> va en el pipeline **antes** de <c>UseAuthentication()</c> —y
/// tiene que seguir ahí, porque su trabajo es cortar avalanchas anónimas antes de que
/// lleguen a validar tokens o a buscar claves en la base—. En ese punto
/// <c>context.User</c> está vacío y no hay ninguna identidad que limitar. Este va
/// **después** de <c>ApiKeyMiddleware</c>, cuando ya se sabe quién llama.</para>
///
/// <para>Los dos conviven: la petición tiene que pasar por ambos. El de IP protege de
/// extraños; este, de los propios.</para>
/// </summary>
public class IdentityRateLimitMiddleware : IMiddleware, ITransientDependency
{
    private readonly IdentityRateLimiterStore _limitadores;
    private readonly RateLimitingOptions _opciones;
    private readonly ILogger<IdentityRateLimitMiddleware> _logger;

    public IdentityRateLimitMiddleware(
        IdentityRateLimiterStore limitadores,
        IOptions<RateLimitingOptions> opciones,
        ILogger<IdentityRateLimitMiddleware> logger)
    {
        _limitadores = limitadores;
        _opciones = opciones.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!_opciones.IsEnabled || !_opciones.IdentityLimitsEnabled)
        {
            await next(context);
            return;
        }

        var (clave, limite) = Identificar(context);

        // Anónimo: no hay identidad que limitar y el de IP ya se ha ocupado de él.
        if (clave == null)
        {
            await next(context);
            return;
        }

        using var permiso = await _limitadores.AdquirirAsync(clave, limite);

        if (permiso.IsAcquired)
        {
            await next(context);
            return;
        }

        await RechazarAsync(context, clave);
    }

    /// <summary>
    /// Quién llama y con qué cuota.
    /// <para>La clave de API manda sobre el usuario: una misma persona puede tener varias
    /// claves, y cada una debe agotar su cuota por separado — si compartieran la del
    /// usuario, un script desbocado dejaría sin servicio a los demás scripts de su dueño
    /// y a su propia sesión del navegador.</para>
    /// </summary>
    private (string? Clave, int Limite) Identificar(HttpContext context)
    {
        var idClave = context.User?.FindFirst(ApiKeyConsts.ClaimTipo)?.Value;
        if (!string.IsNullOrEmpty(idClave))
        {
            return ("apikey:" + idClave, _opciones.ApiKeyPermitLimit);
        }

        var idUsuario = context.User?.FindFirst(AbpClaimTypes.UserId)?.Value;
        if (!string.IsNullOrEmpty(idUsuario))
        {
            return ("user:" + idUsuario, _opciones.UserPermitLimit);
        }

        return (null, 0);
    }

    private async Task RechazarAsync(HttpContext context, string clave)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "desconocida";

        // El patrón de ruta, no la URL con ids reales: si no, cada petición generaría una
        // fila distinta en el panel de tráfico rechazado.
        var endpoint = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                       ?? context.Request.Path.Value
                       ?? "(desconocido)";

        // Se registra en el MISMO acumulador que el límite por IP: el panel de tráfico
        // rechazado no distingue el motivo, pero lo que interesa mirar ahí es "cuánto se
        // está cortando y a quién", y eso es lo mismo en ambos casos.
        context.RequestServices.GetRequiredService<IRateLimitAccumulator>()
            .Record(ip, endpoint);

        _logger.LogWarning(
            "Límite por identidad superado ({Clave}) en {Endpoint} desde {Ip}",
            clave, endpoint, ip);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // ⚠️ CUERPO Y ContentType, igual que el 429 del límite por IP. Fuera de Development,
        // UseErrorPage() convierte cualquier 4xx SIN CUERPO en un 302 a /Error, y el
        // cliente recibiría un redirect sin sentido. Ver CLAUDE.md §5.
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            "{\"error\":{\"code\":\"TooManyRequests\",\"message\":" +
            "\"Has superado tu cuota de peticiones. Espera un momento.\"}}");
    }
}
