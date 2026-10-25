using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Organizaciones;

/// <summary>
/// Las organizaciones vistas desde Usuarios e Invitaciones: para elegir en qué organización
/// entra alguien y para ver dónde está cada persona. Basta el permiso de ver usuarios; crear
/// o editar organizaciones sigue siendo de <see cref="IOrganizacionAppService"/>.
/// </summary>
public interface IMembresiaAppService : IApplicationService
{
    Task<OpcionesDeOrganizacionDto> GetOpcionesAsync();

    /// <summary>Las organizaciones de cada persona pedida (las que quien llama puede ver).</summary>
    Task<List<MembresiaDeUsuarioDto>> GetDeUsuariosAsync(List<Guid> userIds);
}
