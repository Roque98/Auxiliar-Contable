using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace IgasConsole.Organizaciones;

/// <summary>
/// Organizaciones (los clientes del despacho) y quién pertenece a ellas. Pertenecer a una es
/// lo que deja a la gente de un cliente ver y subir su información.
/// </summary>
public interface IOrganizacionAppService : IApplicationService
{
    Task<ListResultDto<OrganizacionDto>> GetListAsync(GetOrganizacionesInput input);

    Task<OrganizacionDetalleDto> GetAsync(Guid id);

    Task<OrganizacionDetalleDto> CreateAsync(GuardarOrganizacionDto input);

    Task<OrganizacionDetalleDto> UpdateAsync(Guid id, GuardarOrganizacionDto input);

    Task DeleteAsync(Guid id);

    /// <summary>Solo personas que quien llama alcanza en la jerarquía (si no, 404).</summary>
    Task<OrganizacionDetalleDto> AgregarMiembroAsync(Guid id, AgregarMiembroDto input);

    Task<OrganizacionDetalleDto> QuitarMiembroAsync(Guid id, Guid userId);

    /// <summary>Cambia el papel de alguien dentro de la organización (Administrador, Miembro o Lector).</summary>
    Task<OrganizacionDetalleDto> CambiarRolDeMiembroAsync(Guid id, Guid userId, CambiarRolEnOrganizacionDto input);

    /// <summary>Personas que se pueden añadir: las que uno alcanza y aún no son miembros.</summary>
    Task<List<MiembroDto>> GetCandidatosAsync(Guid id, string? filtro);

}
