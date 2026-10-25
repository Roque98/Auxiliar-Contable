using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Data;
using Volo.Abp.Identity;

namespace IgasConsole.Identity;

/// <summary>
/// El servicio de roles de ABP con la jerarquía encima: cada uno ve y toca los roles que
/// puede asignar (<see cref="IgasRoles.PuedeAsignar"/>). El Owner no ve el rol del
/// Developer ni el suyo propio; un Administrador, si algún día recibe el permiso de roles,
/// solo vería los de nivel usuario.
///
/// <para>Los listados se filtran en memoria: una organización tiene un puñado de roles,
/// no miles, y así el total y la paginación salen de la misma lista ya filtrada.</para>
/// </summary>
[Authorize(IdentityPermissions.Roles.Default)]
// Sin controlador propio: ABP ya expone este servicio con el suyo (que recibe esta
// sustitución por inyección). Sin esto aparecía DUPLICADO en /api/app/igas-*, y el de
// cuenta se saltaba el límite de peticiones de las rutas de credenciales.
[RemoteService(IsEnabled = false)]
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IIdentityRoleAppService), typeof(IdentityRoleAppService), typeof(IgasIdentityRoleAppService))]
public class IgasIdentityRoleAppService : IdentityRoleAppService
{
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly NivelesDeRoles _niveles;

    public IgasIdentityRoleAppService(
        IdentityRoleManager roleManager,
        IIdentityRoleRepository roleRepository,
        JerarquiaDeUsuarios jerarquia,
        NivelesDeRoles niveles)
        : base(roleManager, roleRepository)
    {
        _jerarquia = jerarquia;
        _niveles = niveles;
    }

    /// <summary>
    /// Crear un rol con su NIVEL (a quién alcanzará quien lo tenga). Solo por debajo del de
    /// quien lo crea: un Owner crea roles de nivel Administrador como mucho; nadie crea uno de
    /// nivel Developer (ver <see cref="NivelDeRol"/>).
    /// </summary>
    [Authorize(IdentityPermissions.Roles.Create)]
    public override async Task<IdentityRoleDto> CreateAsync(IdentityRoleCreateDto input)
    {
        var nivel = await NivelPedidoAsync(input, input.Name);
        input.SetProperty(IgasRoles.PropiedadNivel, (NivelDeRol)nivel);
        var creado = await base.CreateAsync(input);
        await _niveles.InvalidarAsync();
        return creado;
    }

    public override async Task<IdentityRoleDto> GetAsync(Guid id)
    {
        await ExigirAlcanceAsync(id);
        return await base.GetAsync(id);
    }

    public override async Task<ListResultDto<IdentityRoleDto>> GetAllListAsync()
    {
        var roles = await _jerarquia.RolesAlAlcanceAsync();
        return new ListResultDto<IdentityRoleDto>(
            ObjectMapper.Map<List<IdentityRole>, List<IdentityRoleDto>>(roles));
    }

    public override async Task<PagedResultDto<IdentityRoleDto>> GetListAsync(GetIdentityRolesInput input)
    {
        IEnumerable<IdentityRole> roles = await _jerarquia.RolesAlAlcanceAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            roles = roles.Where(r => r.Name.Contains(input.Filter, StringComparison.OrdinalIgnoreCase));
        }

        var lista = roles.ToList();
        var desc = (input.Sorting ?? string.Empty).Trim().EndsWith(" desc", StringComparison.OrdinalIgnoreCase);
        var ordenados = desc
            ? lista.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
            : lista.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

        var pagina = ordenados.Skip(input.SkipCount).Take(input.MaxResultCount).ToList();

        return new PagedResultDto<IdentityRoleDto>(
            lista.Count,
            ObjectMapper.Map<List<IdentityRole>, List<IdentityRoleDto>>(pagina));
    }

    [Authorize(IdentityPermissions.Roles.Update)]
    public override async Task<IdentityRoleDto> UpdateAsync(Guid id, IdentityRoleUpdateDto input)
    {
        var rol = await RoleManager.GetByIdAsync(id);
        await ExigirAlcanceAsync(rol);

        // Los cuatro roles propios conservan su nivel: de él cuelga toda la jerarquía.
        var nivel = rol.IsStatic ? NivelesDeRoles.Leer(rol) : await NivelPedidoAsync(input, input.Name);
        input.SetProperty(IgasRoles.PropiedadNivel, (NivelDeRol)nivel);
        var editado = await base.UpdateAsync(id, input);
        await _niveles.InvalidarAsync();
        return editado;
    }

    /// <summary>El nivel que llega en el formulario, validado: estrictamente por debajo del de quien lo pone.</summary>
    private async Task<int> NivelPedidoAsync(Volo.Abp.ObjectExtending.ExtensibleObject input, string nombre)
    {
        var valor = input.ExtraProperties.TryGetValue(IgasRoles.PropiedadNivel, out var v) ? v?.ToString() : null;
        var pedido = int.TryParse(valor, out var n) ? n
            : Enum.TryParse<NivelDeRol>(valor, ignoreCase: true, out var e) ? (int)e
            : IgasRoles.NivelUsuario;
        if (pedido >= IgasRoles.NivelDeveloper && !string.Equals(nombre, IgasRoles.Developer, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException("El nivel Developer es solo del rol admin.");
        }

        pedido = IgasRoles.NivelValido(nombre, pedido);

        if (!IgasRoles.PuedeGestionar(await _jerarquia.NivelActualAsync(), pedido))
        {
            throw new UserFriendlyException("Solo puedes crear roles de un nivel por debajo del tuyo.");
        }

        return pedido;
    }

    [Authorize(IdentityPermissions.Roles.Delete)]
    public override async Task DeleteAsync(Guid id)
    {
        var rol = await RoleManager.FindByIdAsync(id.ToString());
        if (rol != null)
        {
            await ExigirAlcanceAsync(rol);
        }

        await base.DeleteAsync(id);
        await _niveles.InvalidarAsync();
    }

    private async Task ExigirAlcanceAsync(Guid id)
    {
        await ExigirAlcanceAsync(await RoleManager.GetByIdAsync(id));
    }

    private async Task ExigirAlcanceAsync(IdentityRole rol)
    {
        if (!await _jerarquia.PuedeAsignarAsync(rol.Name))
        {
            throw new EntityNotFoundException(typeof(IdentityRole), rol.Id);
        }
    }
}
