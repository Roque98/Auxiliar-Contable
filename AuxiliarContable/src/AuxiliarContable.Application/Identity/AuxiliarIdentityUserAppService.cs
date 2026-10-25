using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Identity;
using AuxiliarContable.Organizaciones;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable.Identity;

/// <summary>
/// El servicio de usuarios de ABP con la jerarquía de <see cref="AuxiliarRoles"/> encima. Lo
/// usan la pantalla de Usuarios de <c>@abp/ng.identity</c>, el buscador (Ctrl+K) y
/// cualquiera que llame a <c>/api/identity/users</c> — por eso la regla va aquí y no en el
/// front: esconder una fila en Angular no esconde nada.
///
/// <para>Lo que cambia respecto a ABP:</para>
/// <list type="bullet">
/// <item><b>Ver</b>: cada uno ve a quien está por debajo, y a sí mismo. El Developer no
/// aparece para nadie más, ni en listados, ni al pedirlo por id, nombre o correo — 404.</item>
/// <item><b>Tocar</b> (editar, borrar, cambiar roles): solo a quien está estrictamente por
/// debajo.</item>
/// <item><b>Asignar roles</b>: ABP solo deja asignar roles que uno mismo tiene, y con eso el
/// Owner no podría nombrar a un Administrador. Aquí se asigna lo que está por debajo.</item>
/// <item><b>Organización</b>: un cliente (nivel Usuario) se da de alta DENTRO de una
/// organización (<see cref="MembresiasDeOrganizacion"/>). Llega en
/// <c>ExtraProperties["OrganizacionId"]</c> del mismo DTO de ABP, así la regla vale también
/// para quien llame a la API sin pasar por la pantalla.</item>
/// </list>
/// </summary>
// Sin controlador propio: ABP ya expone este servicio con el suyo (que recibe esta
// sustitución por inyección). Sin esto aparecía DUPLICADO en /api/app/auxiliar-*, y el de
// cuenta se saltaba el límite de peticiones de las rutas de credenciales.
[RemoteService(IsEnabled = false)]
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IIdentityUserAppService), typeof(IdentityUserAppService), typeof(AuxiliarIdentityUserAppService))]
public class AuxiliarIdentityUserAppService : IdentityUserAppService
{
    private readonly JerarquiaDeUsuarios _jerarquia;
    private readonly IRepository<IdentityUser, Guid> _consultas;
    private readonly MembresiasDeOrganizacion _membresias;

    /// <summary>La clave en <c>ExtraProperties</c> del alta y la edición.</summary>
    public const string PropiedadOrganizacion = "OrganizacionId";

    public AuxiliarIdentityUserAppService(
        IdentityUserManager userManager,
        IIdentityUserRepository userRepository,
        IIdentityRoleRepository roleRepository,
        IOptions<IdentityOptions> identityOptions,
        IPermissionChecker permissionChecker,
        JerarquiaDeUsuarios jerarquia,
        IRepository<IdentityUser, Guid> consultas,
        MembresiasDeOrganizacion membresias)
        : base(userManager, userRepository, roleRepository, identityOptions, permissionChecker)
    {
        _membresias = membresias;
        _jerarquia = jerarquia;
        _consultas = consultas;
    }

    [Authorize(IdentityPermissions.Users.Default)]
    public override async Task<IdentityUserDto> GetAsync(Guid id)
    {
        await _jerarquia.ExigirVisibleAsync(await UserManager.GetByIdAsync(id));
        return await base.GetAsync(id);
    }

    /// <summary>
    /// Filtra en la CONSULTA, no después: quitar filas de una página ya cortada dejaría
    /// páginas cortas y un total que no cuadra — y el total delataría que hay alguien más.
    ///
    /// <para>El filtro de texto calca el de ABP (<c>EfCoreIdentityUserRepository</c>). El
    /// orden va por lista blanca, como el resto del repositorio: <c>Sorting</c> es texto
    /// libre que llega por la API.</para>
    /// </summary>
    [Authorize(IdentityPermissions.Users.Default)]
    public override async Task<PagedResultDto<IdentityUserDto>> GetListAsync(GetIdentityUsersInput input)
    {
        var ocultos = (await _jerarquia.RolesFueraDeAlcanceAsync()).Select(r => r.Id).ToList();
        var yo = CurrentUser.Id;

        var consulta = (await _consultas.GetQueryableAsync())
            .Where(u => u.Id == yo || !u.Roles.Any(r => ocultos.Contains(r.RoleId)));

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filtro = input.Filter;
            var mayus = filtro.ToUpperInvariant();
            consulta = consulta.Where(u =>
                u.NormalizedUserName.Contains(mayus) ||
                u.NormalizedEmail.Contains(mayus) ||
                (u.Name != null && u.Name.Contains(filtro)) ||
                (u.Surname != null && u.Surname.Contains(filtro)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(filtro)));
        }

        var total = await AsyncExecuter.CountAsync(consulta);

        var pagina = await AsyncExecuter.ToListAsync(
            Ordenar(consulta, input.Sorting)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<IdentityUserDto>(
            total,
            ObjectMapper.Map<List<IdentityUser>, List<IdentityUserDto>>(pagina));
    }

    [Authorize(IdentityPermissions.Users.Default)]
    public override async Task<ListResultDto<IdentityRoleDto>> GetRolesAsync(Guid id)
    {
        await _jerarquia.ExigirVisibleAsync(await UserManager.GetByIdAsync(id));
        return await base.GetRolesAsync(id);
    }

    [Authorize(IdentityPermissions.Users.Default)]
    public override async Task<ListResultDto<IdentityRoleDto>> GetAssignableRolesAsync()
    {
        var roles = await _jerarquia.RolesAlAlcanceAsync();
        return new ListResultDto<IdentityRoleDto>(
            ObjectMapper.Map<List<IdentityRole>, List<IdentityRoleDto>>(roles));
    }

    [Authorize(IdentityPermissions.Users.Create)]
    public override async Task<IdentityUserDto> CreateAsync(IdentityUserCreateDto input)
    {
        var organizacion = LeerOrganizacion(input);
        var cliente = MembresiasDeOrganizacion.NecesitaOrganizacion(await NivelPrevistoAsync(input.RoleNames, []));
        if (cliente)
        {
            // ANTES de crear: ABP guarda al usuario dentro de su CreateAsync, y comprobar
            // después dejaría la cuenta hecha si la unidad de trabajo no es transaccional.
            await _membresias.ExigirExistenteAsync(organizacion);
        }

        var creado = await base.CreateAsync(input);
        if (cliente)
        {
            await _membresias.AgregarAsync(organizacion!.Value, creado.Id);
        }

        return creado;
    }

    [Authorize(IdentityPermissions.Users.Update)]
    public override async Task<IdentityUserDto> UpdateAsync(Guid id, IdentityUserUpdateDto input)
    {
        var usuario = await UserManager.GetByIdAsync(id);
        await _jerarquia.ExigirGestionableAsync(usuario, permitirUnoMismo: true);

        var organizacion = LeerOrganizacion(input);
        var cliente = MembresiasDeOrganizacion.NecesitaOrganizacion(
            await NivelPrevistoAsync(input.RoleNames, await UserManager.GetRolesAsync(usuario)));
        if (cliente && (organizacion.HasValue || !await _membresias.PerteneceAAlgunaAsync(id)))
        {
            // Un cliente sin organización no ve nada: al editarlo se le pide una.
            await _membresias.ExigirExistenteAsync(organizacion);
        }

        var editado = await base.UpdateAsync(id, input);
        if (cliente && organizacion.HasValue)
        {
            await _membresias.AgregarAsync(organizacion.Value, id);
        }

        return editado;
    }

    /// <summary>
    /// El nivel que tendrá el usuario con los roles pedidos, calculado igual que ABP los
    /// aplica: solo si llegan y quien llama puede gestionar roles, y pasados por
    /// <see cref="FilterRolesByCurrentUserAsync"/> (lo que no se alcanza se conserva).
    /// </summary>
    private async Task<int> NivelPrevistoAsync(string[]? pedidos, IList<string> actuales)
    {
        var roles = actuales.AsEnumerable();
        if (pedidos != null && await PermissionChecker.IsGrantedAsync(IdentityPermissions.Users.ManageRoles))
        {
            var nivel = await _jerarquia.NivelActualAsync();
            var mapa = await _jerarquia.NivelesAsync();
            roles = actuales.Where(r => !AuxiliarRoles.PuedeAsignar(nivel, r, mapa))
                .Concat(pedidos.Where(r => AuxiliarRoles.PuedeAsignar(nivel, r, mapa)));
        }

        return AuxiliarRoles.NivelDeRoles(roles, await _jerarquia.NivelesAsync());
    }

    private static Guid? LeerOrganizacion(Volo.Abp.ObjectExtending.ExtensibleObject input)
    {
        return input.ExtraProperties.TryGetValue(PropiedadOrganizacion, out var valor)
               && Guid.TryParse(valor?.ToString(), out var id)
            ? id
            : null;
    }

    [Authorize(IdentityPermissions.Users.Delete)]
    public override async Task DeleteAsync(Guid id)
    {
        var usuario = await UserManager.FindByIdAsync(id.ToString());
        if (usuario != null)
        {
            await _jerarquia.ExigirGestionableAsync(usuario);
        }

        await base.DeleteAsync(id);
    }

    [Authorize(IdentityPermissions.Users.Update)]
    public override async Task UpdateRolesAsync(Guid id, IdentityUserUpdateRolesDto input)
    {
        await _jerarquia.ExigirGestionableAsync(await UserManager.GetByIdAsync(id), permitirUnoMismo: true);
        await base.UpdateRolesAsync(id, input);
    }

    [Authorize(IdentityPermissions.Users.Default)]
    public override async Task<IdentityUserDto> FindByUsernameAsync(string userName)
    {
        return await SoloSiEsVisibleAsync(await UserManager.FindByNameAsync(userName));
    }

    [Authorize(IdentityPermissions.Users.Default)]
    public override async Task<IdentityUserDto> FindByEmailAsync(string email)
    {
        return await SoloSiEsVisibleAsync(await UserManager.FindByEmailAsync(email));
    }

    [Authorize(IdentityPermissions.Users.Default)]
    public override async Task<IdentityUserDto> FindByIdAsync(Guid id)
    {
        return await SoloSiEsVisibleAsync(await UserManager.FindByIdAsync(id.ToString()));
    }

    /// <summary>
    /// Sustituye la regla de ABP («solo roles que el operador tiene») por la jerarquía. Lo
    /// llaman el alta, la edición y el cambio de roles, así que cubre los tres caminos.
    ///
    /// <para>Los roles que el objetivo ya tenía y quedan fuera del alcance del operador se
    /// CONSERVAN, igual que en ABP: quitar lo que uno no puede dar tampoco le toca.</para>
    /// </summary>
    protected override async Task<string[]> FilterRolesByCurrentUserAsync(IdentityUser user, string[] inputRoleNames)
    {
        var nivel = await _jerarquia.NivelActualAsync();
        var mapa = await _jerarquia.NivelesAsync();
        var pedidos = inputRoleNames ?? [];

        var actuales = await UserManager.GetRolesAsync(user);
        var intocables = actuales.Where(r => !AuxiliarRoles.PuedeAsignar(nivel, r, mapa));
        var concedidos = pedidos.Where(r => AuxiliarRoles.PuedeAsignar(nivel, r, mapa));

        return intocables
            .Concat(concedidos)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Un usuario fuera de alcance se responde igual que uno que no existe.</summary>
    private async Task<IdentityUserDto> SoloSiEsVisibleAsync(IdentityUser? usuario)
    {
        if (usuario == null || !await _jerarquia.PuedeVerAsync(usuario))
        {
            return null!;
        }

        return ObjectMapper.Map<IdentityUser, IdentityUserDto>(usuario);
    }

    private static IQueryable<IdentityUser> Ordenar(IQueryable<IdentityUser> consulta, string? orden)
    {
        var texto = (orden ?? string.Empty).Trim().ToLowerInvariant();
        var desc = texto.EndsWith(" desc");
        var campo = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return (campo, desc) switch
        {
            ("email", false) => consulta.OrderBy(u => u.Email).ThenBy(u => u.Id),
            ("email", true) => consulta.OrderByDescending(u => u.Email).ThenBy(u => u.Id),
            ("name", false) => consulta.OrderBy(u => u.Name).ThenBy(u => u.Id),
            ("name", true) => consulta.OrderByDescending(u => u.Name).ThenBy(u => u.Id),
            ("surname", false) => consulta.OrderBy(u => u.Surname).ThenBy(u => u.Id),
            ("surname", true) => consulta.OrderByDescending(u => u.Surname).ThenBy(u => u.Id),
            ("phonenumber", false) => consulta.OrderBy(u => u.PhoneNumber).ThenBy(u => u.Id),
            ("phonenumber", true) => consulta.OrderByDescending(u => u.PhoneNumber).ThenBy(u => u.Id),
            ("creationtime", false) => consulta.OrderBy(u => u.CreationTime).ThenBy(u => u.Id),
            ("creationtime", true) => consulta.OrderByDescending(u => u.CreationTime).ThenBy(u => u.Id),
            ("isactive", false) => consulta.OrderBy(u => u.IsActive).ThenBy(u => u.Id),
            ("isactive", true) => consulta.OrderByDescending(u => u.IsActive).ThenBy(u => u.Id),
            ("username", true) => consulta.OrderByDescending(u => u.UserName).ThenBy(u => u.Id),
            _ => consulta.OrderBy(u => u.UserName).ThenBy(u => u.Id),
        };
    }
}
