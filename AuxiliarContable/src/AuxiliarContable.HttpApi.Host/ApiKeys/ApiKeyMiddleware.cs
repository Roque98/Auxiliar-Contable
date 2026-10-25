using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuxiliarContable.ApiKeys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Autentica una petición a partir de la cabecera <c>X-Api-Key</c>.
///
/// <para>Va DESPUÉS de <c>UseAuthentication</c> y sobrescribe <c>context.User</c>: la
/// autorización de más adelante decide sobre ese principal, así que todos los
/// <c>[Authorize]</c> existentes funcionan sin tocarlos.</para>
///
/// <para>Se eligió un middleware en vez de un esquema de autenticación propio para no
/// tocar la configuración de esquemas del host, que ya reenvía Identity hacia el validador
/// de OpenIddict. El precio es que aquí no hay semántica de «challenge»: si la clave es
/// inválida se responde 401 directamente en vez de dejar que otro esquema lo intente.
/// Para una clave de API eso es lo correcto — quien la manda ya dijo cómo quiere
/// autenticarse.</para>
///
/// <para>Aquí solo queda la <b>fontanería HTTP</b>: leer la cabecera, construir el principal,
/// anotar el uso y escribir el 401. Quién puede autenticar con qué clave lo decide
/// <c>ResolutorDeClaveDeApi</c>, en la capa Application — donde los tests de integración
/// pueden alcanzarlo, cosa que este proyecto no puede.</para>
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _siguiente;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate siguiente, ILogger<ApiKeyMiddleware> logger)
    {
        _siguiente = siguiente;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(ApiKeyConsts.HeaderName, out var valores))
        {
            await _siguiente(context);
            return;
        }

        var clave = valores.ToString();

        var ahora = DateTime.UtcNow;

        var uowManager = context.RequestServices.GetRequiredService<IUnitOfWorkManager>();
        var repositorio = context.RequestServices.GetRequiredService<IApiKeyRepository>();
        var resolutor = context.RequestServices.GetRequiredService<ResolutorDeClaveDeApi>();

        using var uow = uowManager.Begin(requiresNew: true);

        // Toda la regla —existe, no está revocada ni caducada, su inquilino sigue de alta y su
        // usuario también— vive en el resolutor, que es de la capa Application y por tanto SÍ
        // se puede probar: los tests de integración no cargan este proyecto.
        var resuelta = await resolutor.ResolverAsync(clave, ahora);

        // Mismo mensaje para todos los motivos. Distinguirlos le diría a quien prueba claves
        // si acertó con una que existió alguna vez.
        if (resuelta == null)
        {
            _logger.LogWarning(
                "Clave de API rechazada desde {Ip}", context.Connection.RemoteIpAddress);
            await RechazarAsync(context, "La clave de API no es válida.");
            return;
        }

        var currentTenant = context.RequestServices.GetRequiredService<ICurrentTenant>();

        // El inquilino se restaura para TODO lo que viene después: el resolutor solo lo abrió
        // para buscar al usuario, y el resto del pipeline tiene que correr dentro de él.
        using (currentTenant.Change(resuelta.Registro.TenantId))
        {
            context.User = await ConstruirPrincipalAsync(
                context, resuelta.Usuario, resuelta.Registro.Id);

            if (resuelta.Registro.AnotarUso(ahora))
            {
                // Solo cuando de verdad cambió: sin el margen del propio AnotarUso, cada
                // llamada de lectura se convertiría en una escritura.
                await repositorio.UpdateAsync(resuelta.Registro, autoSave: true);
            }

            await uow.CompleteAsync();

            await _siguiente(context);
        }
    }

    /// <summary>
    /// Construye el principal con la MISMA fábrica que usa el login, para que la clave
    /// herede exactamente los claims y permisos que tendría el usuario entrando por la web.
    /// Rehacerlos a mano aquí sería una segunda definición de "quién eres" condenada a
    /// separarse de la primera.
    /// </summary>
    private static async Task<System.Security.Claims.ClaimsPrincipal> ConstruirPrincipalAsync(
        HttpContext context, IdentityUser usuario, Guid idClave)
    {
        var factory = context.RequestServices
            .GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();

        var principal = await factory.CreateAsync(usuario);

        // Marca CUAL clave actuo. La fabrica produce un principal identico al del login
        // —eso es lo que se busca, que los permisos no diverjan—, pero entonces una llamada
        // con clave es indistinguible de una sesion de navegador. Esta marca es lo que
        // permite darle a cada clave su propia cuota de peticiones.
        principal.Identities.First().AddClaim(
            new System.Security.Claims.Claim(ApiKeyConsts.ClaimTipo, idClave.ToString()));

        return principal;
    }

    private static async Task RechazarAsync(HttpContext context, string mensaje)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        // ⚠️ Cuerpo y ContentType, por lo mismo que el 429 del rate limiting y el 403 del
        // bloqueo por IP: fuera de Development, UseErrorPage convierte un 4xx SIN CUERPO en
        // un 302. Un cliente automático recibiría un redirect a una página HTML en vez de
        // un error que pueda interpretar.
        context.Response.ContentType = "application/json; charset=utf-8";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = new { code = "AuxiliarContable:InvalidApiKey", message = mensaje }
        }));
    }
}
