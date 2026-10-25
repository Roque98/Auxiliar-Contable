using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Identity;
using IgasConsole.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace IgasConsole.Organizaciones;

/// <summary>
/// Ver <see cref="IOrganizacionAppService"/>. Es trabajo del equipo (Owner, Administrador,
/// Developer): un cliente no ve esta pantalla, solo el efecto — lo que cuelga de sus
/// organizaciones.
///
/// <para>La jerarquía manda sobre las PERSONAS: en la lista de miembros no sale nadie fuera
/// del alcance de quien mira (el Developer, para el Owner), y solo se añade o quita a quien
/// uno puede gestionar. Lo de fuera de alcance responde 404, como en Usuarios.</para>
/// </summary>
[Authorize(IgasConsolePermissions.Organizaciones.Default)]
public class OrganizacionAppService : IgasConsoleAppService, IOrganizacionAppService
{
    private const int MaxCandidatos = 20;

    private readonly IRepository<Organizacion, Guid> _organizaciones;
    private readonly IIdentityUserRepository _usuarios;
    private readonly JerarquiaDeUsuarios _jerarquia;

    public OrganizacionAppService(
        IRepository<Organizacion, Guid> organizaciones,
        IIdentityUserRepository usuarios,
        JerarquiaDeUsuarios jerarquia)
    {
        _organizaciones = organizaciones;
        _usuarios = usuarios;
        _jerarquia = jerarquia;
    }

    public async Task<ListResultDto<OrganizacionDto>> GetListAsync(GetOrganizacionesInput input)
    {
        var consulta = await ConDetallesAsync();
        if (!string.IsNullOrWhiteSpace(input.Filtro))
        {
            var f = input.Filtro.Trim().ToLower();
            consulta = consulta.Where(o =>
                o.Nombre.ToLower().Contains(f) ||
                (o.Descripcion != null && o.Descripcion.ToLower().Contains(f)));
        }

        var lista = await AsyncExecuter.ToListAsync(consulta.OrderBy(o => o.Nombre));
        var ocultos = await _jerarquia.UsuariosFueraDeAlcanceAsync();
        return new ListResultDto<OrganizacionDto>(lista.Select(o => Mapear(new OrganizacionDto(), o, ocultos)).ToList());
    }

    public async Task<OrganizacionDetalleDto> GetAsync(Guid id)
    {
        return await DetalleAsync(await ObtenerAsync(id));
    }

    public async Task<OrganizacionDetalleDto> CreateAsync(GuardarOrganizacionDto input)
    {
        await ExigirNombreLibreAsync(input.Nombre, null);
        var o = new Organizacion(GuidGenerator.Create(), input.Nombre, input.Descripcion);
        await _organizaciones.InsertAsync(o, autoSave: true);
        return await DetalleAsync(o);
    }

    public async Task<OrganizacionDetalleDto> UpdateAsync(Guid id, GuardarOrganizacionDto input)
    {
        var o = await ObtenerAsync(id);
        await ExigirNombreLibreAsync(input.Nombre, id);
        o.Renombrar(input.Nombre);
        o.CambiarDescripcion(input.Descripcion);
        await GuardarAsync();
        return await DetalleAsync(o);
    }

    public async Task DeleteAsync(Guid id)
    {
        var o = await ObtenerAsync(id);
        // Borrado lógico: el agregado queda y con él sus miembros, pero una organización
        // borrada ya no da acceso a nada (las consultas de alcance parten de las vivas).
        await _organizaciones.DeleteAsync(o, autoSave: true);
    }

    public async Task<OrganizacionDetalleDto> AgregarMiembroAsync(Guid id, AgregarMiembroDto input)
    {
        var o = await ObtenerAsync(id);
        var usuario = await _usuarios.FindAsync(input.UserId)
                      ?? throw new EntityNotFoundException(typeof(IdentityUser), input.UserId);
        await _jerarquia.ExigirGestionableAsync(usuario, permitirUnoMismo: true);

        if (o.AgregarMiembro(usuario.Id, input.Rol))
        {
            await GuardarAsync();
        }

        return await DetalleAsync(o);
    }

    public async Task<OrganizacionDetalleDto> CambiarRolDeMiembroAsync(Guid id, Guid userId, CambiarRolEnOrganizacionDto input)
    {
        var o = await ObtenerAsync(id);
        var usuario = await _usuarios.FindAsync(userId)
                      ?? throw new EntityNotFoundException(typeof(IdentityUser), userId);
        await _jerarquia.ExigirGestionableAsync(usuario, permitirUnoMismo: true);

        o.CambiarRol(userId, input.Rol);
        await GuardarAsync();
        return await DetalleAsync(o);
    }

    public async Task<OrganizacionDetalleDto> QuitarMiembroAsync(Guid id, Guid userId)
    {
        var o = await ObtenerAsync(id);
        var usuario = await _usuarios.FindAsync(userId);
        if (usuario != null)
        {
            await _jerarquia.ExigirGestionableAsync(usuario, permitirUnoMismo: true);
        }
        else if (o.Miembros.All(m => m.UserId != userId))
        {
            throw new EntityNotFoundException(typeof(IdentityUser), userId);
        }

        // Un usuario ya borrado todavía se puede sacar: su fila no le da nada, pero ensucia.
        if (o.QuitarMiembro(userId))
        {
            await GuardarAsync();
        }

        return await DetalleAsync(o);
    }

    public async Task<List<MiembroDto>> GetCandidatosAsync(Guid id, string? filtro)
    {
        var o = await ObtenerAsync(id);
        var ocultos = await _jerarquia.UsuariosFueraDeAlcanceAsync();
        var excluidos = o.Miembros.Select(m => m.UserId).Concat(ocultos).ToHashSet();

        // Se piden de más porque los ocultos y los que ya son miembros se descartan después.
        var usuarios = await _usuarios.GetListAsync(
            sorting: nameof(IdentityUser.UserName),
            maxResultCount: MaxCandidatos + excluidos.Count,
            filter: string.IsNullOrWhiteSpace(filtro) ? null : filtro.Trim());

        return usuarios.Where(u => !excluidos.Contains(u.Id)).Take(MaxCandidatos).Select(Miembro).ToList();
    }

    /// <summary>
    /// ⚠️ NO <c>UpdateAsync</c>: el agregado ya está rastreado (se cargó en esta unidad de
    /// trabajo), y <c>Update</c> marcaría como MODIFICADAS las filas hijas nuevas —su clave
    /// compuesta ya viene puesta—, así que un miembro nuevo no se insertaba.
    /// Guardar los cambios deja que EF los detecte como altas.
    /// </summary>
    private Task GuardarAsync() => CurrentUnitOfWork!.SaveChangesAsync();

    private async Task<IQueryable<Organizacion>> ConDetallesAsync() =>
        await _organizaciones.WithDetailsAsync(o => o.Miembros);

    private async Task<Organizacion> ObtenerAsync(Guid id)
    {
        return await AsyncExecuter.FirstOrDefaultAsync((await ConDetallesAsync()).Where(o => o.Id == id))
               ?? throw new EntityNotFoundException(typeof(Organizacion), id);
    }

    private async Task ExigirNombreLibreAsync(string nombre, Guid? salvo)
    {
        var n = nombre.Trim().ToLower();
        if (await _organizaciones.AnyAsync(o => o.Nombre.ToLower() == n && o.Id != salvo))
        {
            throw new UserFriendlyException($"Ya hay una organización que se llama «{nombre.Trim()}».");
        }
    }

    private async Task<OrganizacionDetalleDto> DetalleAsync(Organizacion o)
    {
        var ocultos = await _jerarquia.UsuariosFueraDeAlcanceAsync();
        var dto = Mapear(new OrganizacionDetalleDto(), o, ocultos);
        var ids = o.Miembros.Select(m => m.UserId).Where(id => !ocultos.Contains(id)).ToList();
        if (ids.Count > 0)
        {
            // Los borrados no vuelven (GetListByIdsAsync respeta el borrado lógico).
            var roles = o.Miembros.ToDictionary(m => m.UserId, m => m.Rol);
            dto.ListaDeMiembros = (await _usuarios.GetListByIdsAsync(ids))
                .OrderBy(u => u.UserName).Select(u => Con(Miembro(u), roles)).ToList();
        }

        dto.Miembros = dto.ListaDeMiembros.Count;
        return dto;
    }

    private static T Mapear<T>(T dto, Organizacion o, ICollection<Guid> ocultos) where T : OrganizacionDto
    {
        dto.Id = o.Id;
        dto.Nombre = o.Nombre;
        dto.Descripcion = o.Descripcion;
        dto.Miembros = o.Miembros.Count(m => !ocultos.Contains(m.UserId));
        return dto;
    }

    private static MiembroDto Con(MiembroDto m, IReadOnlyDictionary<Guid, RolEnOrganizacion> roles)
    {
        m.Rol = roles.TryGetValue(m.UserId, out var r) ? r : RolEnOrganizacion.Miembro;
        return m;
    }

    private static MiembroDto Miembro(IdentityUser u) => new()
    {
        UserId = u.Id,
        UserName = u.UserName,
        NombreCompleto = string.Join(' ', new[] { u.Name, u.Surname }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } n ? n : null,
        Email = u.Email.EndsWith("@" + Seguridad.SeguridadConsts.DominioProvisional, StringComparison.OrdinalIgnoreCase) ? null : u.Email,
    };
}
