using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Organizaciones;

/// <summary>
/// Un cliente del despacho y las personas que pueden ver su información (su propia gente,
/// con un papel dentro de la organización). Genérica a propósito: una persona física, una
/// empresa o un grupo de empresas.
/// </summary>
public class Organizacion : FullAuditedAggregateRoot<Guid>
{
    public string Nombre { get; private set; } = string.Empty;

    public string? Descripcion { get; private set; }

    public ICollection<OrganizacionMiembro> Miembros { get; private set; } = [];

    protected Organizacion()
    {
    }

    public Organizacion(Guid id, string nombre, string? descripcion) : base(id)
    {
        Renombrar(nombre);
        CambiarDescripcion(descripcion);
    }

    public void Renombrar(string nombre)
    {
        Nombre = Check.NotNullOrWhiteSpace(nombre, nameof(nombre), OrganizacionConsts.MaxNombreLength).Trim();
    }

    public void CambiarDescripcion(string? descripcion)
    {
        Descripcion = string.IsNullOrWhiteSpace(descripcion)
            ? null
            : Check.Length(descripcion.Trim(), nameof(descripcion), OrganizacionConsts.MaxDescripcionLength);
    }

    /// <summary>False si ya era miembro (entonces no cambia su papel).</summary>
    public bool AgregarMiembro(Guid userId, RolEnOrganizacion rol = RolEnOrganizacion.Miembro)
    {
        if (Miembros.Any(m => m.UserId == userId))
        {
            return false;
        }

        Miembros.Add(new OrganizacionMiembro(Id, userId, rol));
        return true;
    }

    /// <summary>False si no era miembro. No deja a la organización sin administrador si tenía.</summary>
    public bool QuitarMiembro(Guid userId)
    {
        var miembro = Miembros.FirstOrDefault(m => m.UserId == userId);
        if (miembro == null)
        {
            return false;
        }

        ExigirOtroAdministrador(miembro);
        return Miembros.Remove(miembro);
    }

    /// <summary>Cambia el papel de alguien dentro de la organización.</summary>
    public void CambiarRol(Guid userId, RolEnOrganizacion rol)
    {
        var miembro = Miembros.FirstOrDefault(m => m.UserId == userId)
                      ?? throw new BusinessException("AuxiliarContable:NoEsMiembro");
        if (miembro.Rol == rol)
        {
            return;
        }

        if (rol != RolEnOrganizacion.Administrador)
        {
            ExigirOtroAdministrador(miembro);
        }

        miembro.Rol = rol;
    }

    public RolEnOrganizacion? RolDe(Guid userId) => Miembros.FirstOrDefault(m => m.UserId == userId)?.Rol;

    /// <summary>
    /// Quien era el ÚLTIMO administrador no puede dejar de serlo: la organización se quedaría
    /// sin quien gestione a su gente (el equipo del despacho sí podría, desde Organizaciones).
    /// </summary>
    private void ExigirOtroAdministrador(OrganizacionMiembro miembro)
    {
        if (miembro.Rol == RolEnOrganizacion.Administrador
            && Miembros.Count(m => m.Rol == RolEnOrganizacion.Administrador) == 1)
        {
            throw new BusinessException("AuxiliarContable:UltimoAdministradorDeOrganizacion");
        }
    }
}

public class OrganizacionMiembro : Entity
{
    public Guid OrganizacionId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Su papel en ESTA organización (ver <see cref="RolEnOrganizacion"/>).</summary>
    public RolEnOrganizacion Rol { get; internal set; } = RolEnOrganizacion.Miembro;

    protected OrganizacionMiembro()
    {
    }

    internal OrganizacionMiembro(Guid organizacionId, Guid userId, RolEnOrganizacion rol)
    {
        OrganizacionId = organizacionId;
        UserId = userId;
        Rol = rol;
    }

    public override object[] GetKeys() => [OrganizacionId, UserId];
}
