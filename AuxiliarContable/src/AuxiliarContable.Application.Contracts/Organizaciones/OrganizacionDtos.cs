using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AuxiliarContable.Organizaciones;

public class OrganizacionDto : EntityDto<Guid>
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    /// <summary>Solo las que quien mira alcanza en la jerarquía.</summary>
    public int Miembros { get; set; }
}

public class MiembroDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? NombreCompleto { get; set; }
    public string? Email { get; set; }

    /// <summary>Su papel en la organización.</summary>
    public RolEnOrganizacion Rol { get; set; } = RolEnOrganizacion.Miembro;
}

public class OrganizacionDetalleDto : OrganizacionDto
{
    public List<MiembroDto> ListaDeMiembros { get; set; } = [];
}

public class GuardarOrganizacionDto
{
    [Required]
    [StringLength(OrganizacionConsts.MaxNombreLength)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(OrganizacionConsts.MaxDescripcionLength)]
    public string? Descripcion { get; set; }
}

public class AgregarMiembroDto
{
    public Guid UserId { get; set; }

    public RolEnOrganizacion Rol { get; set; } = RolEnOrganizacion.Miembro;
}

public class CambiarRolEnOrganizacionDto
{
    public RolEnOrganizacion Rol { get; set; }
}

/// <summary>Una organización de la que soy miembro, con mi papel en ella («Mi organización»).</summary>
public class MiOrganizacionDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public RolEnOrganizacion MiRol { get; set; }
    public bool PuedoGestionarPersonas { get; set; }

    /// <summary>Solo si la puedo gestionar.</summary>
    public List<MiembroDto> Miembros { get; set; } = [];

    /// <summary>Invitaciones a esta organización que aún no se aceptan (solo si la puedo gestionar).</summary>
    public List<InvitacionPendienteDto> Pendientes { get; set; } = [];
}

public class InvitacionPendienteDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public RolEnOrganizacion Rol { get; set; }
    public DateTime CreationTime { get; set; }
}

public class InvitarAMiOrganizacionDto
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    [System.ComponentModel.DataAnnotations.StringLength(256)]
    public string Email { get; set; } = string.Empty;

    public RolEnOrganizacion Rol { get; set; } = RolEnOrganizacion.Miembro;
}

public class GetOrganizacionesInput
{
    public string? Filtro { get; set; }
}

/// <summary>Lo que necesita un formulario de alta para elegir la organización.</summary>
public class OpcionesDeOrganizacionDto
{
    public List<OrganizacionResumenDto> Organizaciones { get; set; } = [];

    /// <summary>Si quien pregunta puede crear una organización desde el mismo formulario.</summary>
    public bool PuedeCrear { get; set; }

    /// <summary>A partir de qué nivel de rol NO hace falta organización (el equipo).</summary>
    public int NivelSinOrganizacion { get; set; } = 1;
}

public class MembresiaDeUsuarioDto
{
    public Guid UserId { get; set; }
    public List<OrganizacionResumenDto> Organizaciones { get; set; } = [];
}

public class OrganizacionResumenDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Quien pregunta puede subir aquí (el equipo, siempre; un cliente, según su papel).</summary>
    public bool PuedeSubir { get; set; }
}
