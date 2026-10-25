using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace IgasConsole.Pages.Account;

/// <summary>
/// Al cerrar sesión, borra también su fila.
///
/// <para>Sin esto la sesión seguiría apareciendo en la lista aunque ya no valga: la purga
/// por inactividad acabaría quitándola, pero treinta días después. Y una lista de sesiones
/// que enseña sesiones muertas es peor que no tenerla, porque lo que se busca ahí es
/// reconocer lo que NO deberías ver.</para>
///
/// <para>Solo se sustituye el modelo, no la vista.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IgasConsoleLogoutModel), typeof(LogoutModel))]
public class IgasConsoleLogoutModel : LogoutModel
{
    private readonly IIdentitySessionRepository _sesiones;

    public IgasConsoleLogoutModel(IIdentitySessionRepository sesiones)
    {
        _sesiones = sesiones;
    }

    public override async Task<IActionResult> OnGetAsync()
    {
        // Se lee ANTES de cerrar: después el principal ya no tiene claims y no habría
        // forma de saber cuál era.
        var sessionId = CurrentUser.FindSessionId();

        var resultado = await base.OnGetAsync();

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var sesion = await _sesiones.FindAsync(sessionId!);
            if (sesion != null)
            {
                await _sesiones.DeleteAsync(sesion);
            }
        }

        return resultado;
    }
}
