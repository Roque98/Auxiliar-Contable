using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace IgasConsole.Sessions;

public class UserSessionDto
{
    /// <summary>
    /// Id de la FILA, no el identificador de sesión.
    /// <para>El <c>SessionId</c> real no se expone: viaja dentro del token y sirve para
    /// identificar una sesión viva. No hace falta enseñarlo para poder cerrarla, y todo lo
    /// que no se enseña no se puede filtrar.</para>
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>Navegador o dispositivo, tal como lo declaró al entrar.</summary>
    public string? Device { get; set; }

    public string? DeviceInfo { get; set; }

    public string? IpAddresses { get; set; }

    public DateTime SignedIn { get; set; }

    /// <summary>Última actividad, con la precisión que permite el refresco.</summary>
    public DateTime? LastAccessed { get; set; }

    /// <summary>
    /// True si es LA sesión desde la que se está mirando la lista.
    /// <para>Se marca en el servidor para que la pantalla pueda avisar de que cerrarla te
    /// echa a ti mismo, en vez de dejar que lo descubras pulsando.</para>
    /// </summary>
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Sesiones abiertas del USUARIO ACTUAL. Sin permiso propio, igual que la foto de perfil:
/// cualquiera con sesión ve las suyas y solo las suyas — el id de usuario sale de la
/// sesión, nunca de la petición.
/// </summary>
public interface IUserSessionAppService : IApplicationService
{
    Task<List<UserSessionDto>> GetListAsync();

    /// <summary>Cierra una sesión concreta. Puede ser la propia.</summary>
    Task RevokeAsync(Guid id);

    /// <summary>Cierra todas menos aquella desde la que se llama.</summary>
    Task RevokeOthersAsync();
}
