using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SimpleStateChecking;

namespace IgasConsole.Identity;

/// <summary>
/// La pantalla de permisos (el modal de «Permisos» de roles y usuarios) con la jerarquía
/// encima.
///
/// <para>ABP ya impide conceder un permiso que uno no tiene (verificado en
/// <c>refs/abp</c>, <c>FilterInputPermissionsByCurrentUserAsync</c>), así que el Owner no
/// puede repartir permisos técnicos. Lo que ABP NO mira es <b>a quién</b> se le cambian: con
/// el permiso de gestionar permisos de roles, el Owner podría abrir el rol <c>admin</c> y
/// quitarle al Developer lo que el propio Owner tiene — ABP se lo devolvería en la siguiente
/// migración, pero mientras tanto el Developer estaría sin acceso. Y podría abrir el suyo.</para>
///
/// <para>Aquí se comprueba el destinatario antes de leer o escribir nada:</para>
/// <list type="bullet">
/// <item>Rol (<c>R</c>): solo roles al alcance (<see cref="IgasRoles.PuedeAsignar"/>).</item>
/// <item>Usuario (<c>U</c>): solo usuarios que uno puede gestionar; uno mismo no.</item>
/// <item>Cualquier otro proveedor (clientes OAuth, etc.): solo el Developer.</item>
/// </list>
/// </summary>
// Sin controlador propio: ABP ya expone este servicio con el suyo (que recibe esta
// sustitución por inyección). Sin esto aparecía DUPLICADO en /api/app/igas-*, y el de
// cuenta se saltaba el límite de peticiones de las rutas de credenciales.
[RemoteService(IsEnabled = false)]
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IPermissionAppService), typeof(PermissionAppService), typeof(IgasPermissionAppService))]
public class IgasPermissionAppService : PermissionAppService
{
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly IdentityUserManager _usuarios;

    public IgasPermissionAppService(
        IPermissionManager permissionManager,
        IPermissionChecker permissionChecker,
        IPermissionDefinitionManager permissionDefinitionManager,
        IResourcePermissionManager resourcePermissionManager,
        IResourcePermissionGrantRepository resourcePermissionGrantRepository,
        IOptions<PermissionManagementOptions> options,
        ISimpleStateCheckerManager<PermissionDefinition> simpleStateCheckerManager,
        JerarquiaDeUsuarios jerarquia,
        IdentityUserManager usuarios)
        : base(
            permissionManager,
            permissionChecker,
            permissionDefinitionManager,
            resourcePermissionManager,
            resourcePermissionGrantRepository,
            options,
            simpleStateCheckerManager)
    {
        _jerarquia = jerarquia;
        _usuarios = usuarios;
    }

    public override async Task<GetPermissionListResultDto> GetAsync(string providerName, string providerKey)
    {
        await ExigirDestinatarioAsync(providerName, providerKey);
        return await base.GetAsync(providerName, providerKey);
    }

    public override async Task<GetPermissionListResultDto> GetByGroupAsync(string groupName, string providerName, string providerKey)
    {
        await ExigirDestinatarioAsync(providerName, providerKey);
        return await base.GetByGroupAsync(groupName, providerName, providerKey);
    }

    public override async Task UpdateAsync(string providerName, string providerKey, UpdatePermissionsDto input)
    {
        await ExigirDestinatarioAsync(providerName, providerKey);
        await base.UpdateAsync(providerName, providerKey, input);
    }

    public override async Task<GetResourcePermissionWithProviderListResultDto> GetResourceByProviderAsync(
        string resourceName, string resourceKey, string providerName, string providerKey)
    {
        await ExigirDestinatarioAsync(providerName, providerKey);
        return await base.GetResourceByProviderAsync(resourceName, resourceKey, providerName, providerKey);
    }

    public override async Task UpdateResourceAsync(string resourceName, string resourceKey, UpdateResourcePermissionsDto input)
    {
        await ExigirDestinatarioAsync(input.ProviderName, input.ProviderKey);
        await base.UpdateResourceAsync(resourceName, resourceKey, input);
    }

    public override async Task DeleteResourceAsync(string resourceName, string resourceKey, string providerName, string providerKey)
    {
        await ExigirDestinatarioAsync(providerName, providerKey);
        await base.DeleteResourceAsync(resourceName, resourceKey, providerName, providerKey);
    }

    private async Task ExigirDestinatarioAsync(string providerName, string providerKey)
    {
        var nivel = await _jerarquia.NivelActualAsync();
        if (nivel >= IgasRoles.NivelDeveloper)
        {
            return;
        }

        switch (providerName)
        {
            case RolePermissionValueProvider.ProviderName:
                if (!IgasRoles.PuedeAsignar(nivel, providerKey, await _jerarquia.NivelesAsync()))
                {
                    throw new EntityNotFoundException(typeof(IdentityRole), providerKey);
                }

                return;

            case UserPermissionValueProvider.ProviderName:
                var usuario = Guid.TryParse(providerKey, out var id)
                    ? await _usuarios.FindByIdAsync(id.ToString())
                    : null;

                if (usuario == null)
                {
                    throw new EntityNotFoundException(typeof(IdentityUser), providerKey);
                }

                await _jerarquia.ExigirGestionableAsync(usuario);
                return;

            default:
                throw new EntityNotFoundException(providerName);
        }
    }
}
