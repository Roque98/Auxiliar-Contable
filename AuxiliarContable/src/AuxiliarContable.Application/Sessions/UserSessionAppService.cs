using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AuxiliarContable.Sessions;

/// <summary>
/// Lista y cierra las sesiones abiertas del usuario actual.
///
/// <para>Complementa al segundo factor y a las claves de API: aquel controla QUIÉN entra,
/// esto controla QUÉ sigue dentro. Si alguien te robó la contraseña y entró antes de que
/// activaras el 2FA, cambiarla no lo echa — su cookie sigue viva. Cerrar la sesión sí.</para>
/// </summary>
[Authorize]
public class UserSessionAppService : AuxiliarContableAppService, IUserSessionAppService
{
    private readonly IIdentitySessionRepository _sesiones;

    public UserSessionAppService(IIdentitySessionRepository sesiones)
    {
        _sesiones = sesiones;
    }

    public async Task<List<UserSessionDto>> GetListAsync()
    {
        var userId = CurrentUser.GetId();
        var actual = SesionActual();

        var filas = await _sesiones.GetListAsync(userId: userId);

        return filas
            // La actual primero, y el resto por actividad reciente: la que más interesa
            // revisar es la que lleva más tiempo abierta sin que la reconozcas.
            .OrderByDescending(s => s.SessionId == actual)
            .ThenByDescending(s => s.LastAccessed ?? s.SignedIn)
            .Select(s => new UserSessionDto
            {
                Id = s.Id,
                Device = s.Device,
                DeviceInfo = s.DeviceInfo,
                IpAddresses = s.IpAddresses,
                SignedIn = s.SignedIn,
                LastAccessed = s.LastAccessed,
                IsCurrent = s.SessionId == actual
            })
            .ToList();
    }

    public async Task RevokeAsync(Guid id)
    {
        var sesion = await _sesiones.FindAsync(id);

        if (sesion == null)
        {
            // Ya no está: cerrar algo que no existe es exactamente lo que se pidió.
            return;
        }

        // Se comprueba el dueño. Sin esto, cualquiera con sesión podría echar a otro con
        // solo adivinar un id.
        if (sesion.UserId != CurrentUser.GetId())
        {
            throw new UserFriendlyException("Esa sesión no es tuya.");
        }

        await _sesiones.DeleteAsync(sesion);
    }

    public async Task RevokeOthersAsync()
    {
        var actual = SesionActual();

        if (actual.IsNullOrWhiteSpace())
        {
            // Sin saber cuál es la propia no se puede excluir, y borrarlas TODAS incluiría
            // la de quien está pulsando el botón: se quedaría fuera sin haberlo pedido.
            throw new UserFriendlyException(
                "No se pudo identificar tu sesión actual. Vuelve a iniciar sesión e inténtalo de nuevo.");
        }

        var mia = await _sesiones.FindAsync(actual!);

        await _sesiones.DeleteAllAsync(CurrentUser.GetId(), exceptSessionId: mia?.Id);
    }

    /// <summary>
    /// Identificador de la sesión desde la que llega la petición, sacado del claim que
    /// viaja en la cookie y en el token.
    /// </summary>
    private string? SesionActual()
    {
        return CurrentUser.FindSessionId();
    }
}
