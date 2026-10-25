using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace IgasConsole.Invitaciones;

/// <summary>
/// Invitar por correo con un rol ya decidido. Exige el mismo permiso que crear usuarios
/// (<c>AbpIdentity.Users.Create</c>) y la misma jerarquía que asignar roles: solo se invita a
/// roles por debajo del propio. Las invitaciones de alguien fuera de alcance no se ven.
/// </summary>
public interface IInvitacionAppService : IApplicationService
{
    Task<OpcionesDeInvitacionDto> GetOpcionesAsync();

    Task<PagedResultDto<InvitacionDto>> GetListAsync(GetInvitacionesInput input);

    Task<InvitacionDto> CreateAsync(CrearInvitacionDto input);

    /// <summary>Manda un enlace nuevo; el anterior deja de valer. Revive las vencidas.</summary>
    Task<InvitacionDto> ReenviarAsync(Guid id);

    Task<InvitacionDto> RevocarAsync(Guid id);
}

/// <summary>
/// La parte pública: la abre quien recibió el correo, sin sesión. Va en las rutas con
/// límite de credenciales (ver <c>RateLimitingServiceCollectionExtensions</c>).
/// </summary>
public interface IRegistroAppService : IApplicationService
{
    Task<InvitacionPublicaDto> ConsultarAsync(TokenDeInvitacionDto input);

    Task AceptarAsync(AceptarInvitacionDto input);
}
