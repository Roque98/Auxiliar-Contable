using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.Security;
using Microsoft.AspNetCore.Http;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Corta las peticiones que vienen de una IP vetada.
/// </summary>
public class BlockedIpMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly IBlockedIpCache _bloqueadas;

    public BlockedIpMiddleware(RequestDelegate siguiente, IBlockedIpCache bloqueadas)
    {
        _siguiente = siguiente;
        _bloqueadas = bloqueadas;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // RemoteIpAddress ya es la IP real gracias a ForwardedHeadersOptions: detrás de
        // nginx, sin eso todo el tráfico parecería venir de 127.0.0.1 y bloquear una IP
        // habría bloqueado a todo el mundo o a nadie.
        var ip = context.Connection.RemoteIpAddress?.ToString();

        if (!_bloqueadas.EstaBloqueada(ip))
        {
            await _siguiente(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        // ⚠️ ESCRIBIR CUERPO Y CONTENT-TYPE NO ES DECORATIVO. Fuera de Development el host
        // registra UseErrorPage(), que es UseStatusCodePagesWithRedirects: convierte
        // cualquier respuesta 400-599 SIN CUERPO en un 302 a la página de error. Un 403 a
        // secas funcionaría en local y en producción se convertiría en un redirect absurdo
        // — exactamente lo que pasó con el 429 del rate limiting. StatusCodePages se
        // abstiene si la respuesta ya trae cuerpo.
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = new
            {
                code = "AuxiliarContable:IpBlocked",
                message = "Tu dirección IP está bloqueada en esta aplicación."
            }
        }));
    }
}
