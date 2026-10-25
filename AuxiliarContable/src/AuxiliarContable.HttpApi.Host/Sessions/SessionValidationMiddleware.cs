using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AuxiliarContable.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Comprueba que la sesión de la petición sigue viva; si la cerraron, la petición pasa a
/// ser anónima.
///
/// <para>Sin esto, «cerrar sesión en otro dispositivo» sería un botón decorativo: la cookie
/// y el access token de ese dispositivo siguen siendo criptográficamente válidos hasta que
/// caducan, y nadie los estaría comprobando contra nada.</para>
///
/// <para>⚠️ Al fallar NO se responde 401 a mano: se vacía <c>context.User</c> y la petición
/// sigue como anónima. Así el resto del pipeline hace lo que ya sabe hacer —un endpoint
/// público responde, uno protegido rechaza— en vez de inventar aquí un camino de error
/// nuevo que habría que mantener aparte.</para>
/// </summary>
public class SessionValidationMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly ILogger<SessionValidationMiddleware> _logger;

    public SessionValidationMiddleware(
        RequestDelegate siguiente,
        ILogger<SessionValidationMiddleware> logger)
    {
        _siguiente = siguiente;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sessionId = context.User?.FindFirst(AbpClaimTypes.SessionId)?.Value;

        if (sessionId.IsNullOrWhiteSpace())
        {
            // Sin claim no hay nada que validar. Pasa por aquí todo lo anónimo y también
            // las claves de API, que autentican por otro camino y no abren sesión.
            await _siguiente(context);
            return;
        }

        var uowManager = context.RequestServices.GetRequiredService<IUnitOfWorkManager>();
        var sesiones = context.RequestServices.GetRequiredService<IIdentitySessionRepository>();

        using var uow = uowManager.Begin(requiresNew: true);

        var sesion = await sesiones.FindAsync(sessionId!);

        if (sesion == null)
        {
            _logger.LogInformation(
                "Sesión cerrada o caducada; la petición continúa como anónima.");

            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            await _siguiente(context);
            return;
        }

        // La marca de último acceso se refresca con margen: escribirla en cada petición
        // convertiría toda llamada de lectura en una escritura, y el camino caliente aquí
        // son TODAS las peticiones autenticadas.
        var ahora = DateTime.UtcNow;
        if (sesion.LastAccessed == null ||
            ahora - sesion.LastAccessed.Value >= TimeSpan.FromMinutes(SessionConsts.LastAccessedRefreshMinutes))
        {
            sesion.UpdateLastAccessedTime(ahora);
            await sesiones.UpdateAsync(sesion, autoSave: true);
        }

        await uow.CompleteAsync();

        await _siguiente(context);
    }
}
