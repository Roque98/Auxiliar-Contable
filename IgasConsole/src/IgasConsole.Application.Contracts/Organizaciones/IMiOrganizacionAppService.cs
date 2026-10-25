using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace IgasConsole.Organizaciones;

/// <summary>
/// «Mi organización»: lo que un CLIENTE administra de la suya. Lo usa quien tenga el papel de
/// Administrador en una organización (<see cref="RolEnOrganizacion"/>): invitar, quitar y
/// cambiar el papel de su gente. Nunca toca otras organizaciones, ni al equipo de ZIA, ni
/// roles o permisos globales. Lo que no puede gestionar responde 404.
/// </summary>
public interface IMiOrganizacionAppService : IApplicationService
{
    /// <summary>Las organizaciones de las que soy miembro, con mi papel (y su gente si las administro).</summary>
    Task<List<MiOrganizacionDto>> GetListAsync();

    Task<MiOrganizacionDto> InvitarAsync(Guid organizacionId, InvitarAMiOrganizacionDto input);

    Task<MiOrganizacionDto> CambiarRolAsync(Guid organizacionId, Guid userId, CambiarRolEnOrganizacionDto input);

    Task<MiOrganizacionDto> QuitarAsync(Guid organizacionId, Guid userId);

    Task<MiOrganizacionDto> RevocarInvitacionAsync(Guid organizacionId, Guid invitacionId);
}
