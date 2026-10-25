using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Security.Claims;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Copia el <c>session_id</c> de la cookie al principal que ABP reconstruye.
///
/// <para>⚠️ SIN ESTO EL BOTÓN DE CERRAR SESIÓN NO SIRVE PARA EL FRONT, y no se ve en
/// ninguna prueba de la API: el endpoint de autorización de OpenIddict no reutiliza el
/// principal de la cookie, sino que llama a <c>SignInManager.CreateUserPrincipalAsync(user)</c>
/// y lo construye <b>desde el usuario</b>. Los claims añadidos al iniciar sesión —el nuestro
/// entre ellos— se quedan por el camino, y el access token sale sin identificador de sesión.
/// Resultado: la cookie sí se cortaba al revocar, pero el token del SPA seguía entrando.</para>
///
/// <para>Se descubrió mirando los claims de un token real, no leyendo el código: por eso
/// vale la pena decodificar el token en vez de dar por hecho que el claim viaja.</para>
///
/// <para>Un contribuidor es el punto correcto porque <c>AbpUserClaimsPrincipalFactory</c>
/// los ejecuta en TODA construcción de principal, venga de donde venga.</para>
/// </summary>
public class SessionIdClaimContributor : IAbpClaimsPrincipalContributor, ITransientDependency
{
    public Task ContributeAsync(AbpClaimsPrincipalContributorContext context)
    {
        var identidad = context.ClaimsPrincipal.Identities.FirstOrDefault();

        if (identidad == null || identidad.FindFirst(AbpClaimTypes.SessionId) != null)
        {
            // Ya lo lleva: no hay nada que copiar. Pasa en el propio login, donde el claim
            // se añade después con SignInWithClaimsAsync.
            return Task.CompletedTask;
        }

        var httpContext = context.GetRequiredService<IHttpContextAccessor>().HttpContext;
        var sessionId = httpContext?.User?.FindFirst(AbpClaimTypes.SessionId)?.Value;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            identidad.AddClaim(new Claim(AbpClaimTypes.SessionId, sessionId));
        }

        return Task.CompletedTask;
    }
}
