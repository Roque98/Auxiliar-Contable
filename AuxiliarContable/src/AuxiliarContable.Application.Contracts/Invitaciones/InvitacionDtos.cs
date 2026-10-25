using Volo.Abp.Auditing;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Identity;
using Volo.Abp.Validation;

namespace AuxiliarContable.Invitaciones;

public class InvitacionDto : EntityDto<Guid>
{
    public string Email { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public string RolEtiqueta { get; set; } = string.Empty;
    public EstadoInvitacion Estado { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? ExpiraEn { get; set; }
    public DateTime? UltimoEnvio { get; set; }
    public int Envios { get; set; }
    public DateTime? AceptadaEn { get; set; }

    /// <summary>Usuario de quien invitó. Vacío si ya no existe.</summary>
    public string? InvitadoPor { get; set; }

    /// <summary>Todavía en cola: el correo aún no ha salido.</summary>
    public bool EnCola { get; set; }

    public Guid? OrganizacionId { get; set; }

    /// <summary>Vacío para el equipo, o si la organización se borró.</summary>
    public string? Organizacion { get; set; }
}

public class GetInvitacionesInput : PagedAndSortedResultRequestDto
{
    public string? Filtro { get; set; }

    public EstadoInvitacion? Estado { get; set; }
}

public class CrearInvitacionDto
{
    [Required]
    [EmailAddress]
    [StringLength(InvitacionConsts.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(InvitacionConsts.MaxRolLength)]
    public string Rol { get; set; } = string.Empty;

    /// <summary>Obligatoria si el rol es de cliente (nivel Usuario); se ignora para el equipo.</summary>
    public Guid? OrganizacionId { get; set; }

    /// <summary>Su papel en esa organización.</summary>
    public AuxiliarContable.Organizaciones.RolEnOrganizacion RolEnOrganizacion { get; set; } = AuxiliarContable.Organizaciones.RolEnOrganizacion.Miembro;
}

public class RolInvitableDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Etiqueta { get; set; } = string.Empty;
    public int Nivel { get; set; }
}

public class OpcionesDeInvitacionDto
{
    /// <summary>Sin esto las invitaciones no pueden salir; la pantalla lo avisa.</summary>
    public bool CorreoConfigurado { get; set; }

    public int DiasDeVigencia { get; set; } = InvitacionConsts.DiasDeVigencia;

    /// <summary>Los roles que quien pregunta puede dar, de mayor a menor.</summary>
    public List<RolInvitableDto> Roles { get; set; } = [];
}

/// <summary>El token va en el cuerpo (POST), nunca en la URL: así no queda en los logs.</summary>
public class TokenDeInvitacionDto
{
    [Required]
    [StringLength(100)]
    // Fuera del registro de auditoría: ABP guarda los parámetros de cada llamada.
    [DisableAuditing]
    public string Token { get; set; } = string.Empty;
}

public class InvitacionPublicaDto
{
    public bool Vigente { get; set; }
    public string? Email { get; set; }
    public string? RolEtiqueta { get; set; }
    public string? UsuarioSugerido { get; set; }

    /// <summary>La organización a la que entra, si es un cliente.</summary>
    public string? Organizacion { get; set; }
}

public class AceptarInvitacionDto : TokenDeInvitacionDto
{
    [Required]
    [DynamicStringLength(typeof(IdentityUserConsts), nameof(IdentityUserConsts.MaxUserNameLength))]
    public string UserName { get; set; } = string.Empty;

    [DynamicStringLength(typeof(IdentityUserConsts), nameof(IdentityUserConsts.MaxNameLength))]
    public string? Name { get; set; }

    [DynamicStringLength(typeof(IdentityUserConsts), nameof(IdentityUserConsts.MaxSurnameLength))]
    public string? Surname { get; set; }

    [Required]
    [DynamicStringLength(typeof(IdentityUserConsts), nameof(IdentityUserConsts.MaxPasswordLength))]
    // Fuera del registro de auditoría: ABP guarda los parámetros de cada llamada.
    [DisableAuditing]
    public string Password { get; set; } = string.Empty;
}
