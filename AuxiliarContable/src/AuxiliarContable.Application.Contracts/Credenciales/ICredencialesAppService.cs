using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;

namespace AuxiliarContable.Credenciales;

/// <summary>
/// La e.firma y la CIEC de un cliente, que entrega al contratar el servicio.
///
/// <list type="bullet">
/// <item><b>Ver qué hay</b> (RFC, número de certificado, vigencia): quien ve al cliente.</item>
/// <item><b>Guardar, ver la contraseña, descargar y borrar</b>: el administrador de la organización del cliente y el
/// equipo del despacho. Ver o descargar pide además el NIP de la persona (o la contraseña de su cuenta).</item>
/// </list>
///
/// <para>Nada de esto entra al registro de auditoría general ni al chat: tiene su propia bitácora, sin el dato.</para>
/// </summary>
public interface ICredencialesAppService : IApplicationService
{
    Task<CredencialesDto> GetAsync(Guid organizacionId);

    Task<CredencialesDto> GuardarEfirmaAsync(Guid organizacionId, GuardarEfirmaDto input);

    Task<CredencialesDto> GuardarCiecAsync(Guid organizacionId, GuardarCiecDto input);

    Task<CredencialesDto> BorrarAsync(Guid organizacionId, TipoDeCredencial tipo);

    /// <summary>La contraseña de la e.firma o la de la CIEC, si el NIP (o la contraseña de la cuenta) es correcto.</summary>
    Task<ContrasenaReveladaDto> RevelarAsync(Guid organizacionId, RevelarCredencialDto input);

    /// <summary>El .cer o el .key, en base64. El .key pide NIP; el .cer es público y no.</summary>
    Task<ArchivoDeCredencialDto> DescargarAsync(Guid organizacionId, DescargarCredencialDto input);

    /// <summary>Pone o cambia el NIP de quien está en sesión; pide la contraseña de su cuenta.</summary>
    Task PonerNipAsync(PonerNipDto input);

    Task<List<UsoDeCredencialDto>> GetBitacoraAsync(Guid organizacionId);
}

public class CredencialesDto
{
    public Guid OrganizacionId { get; set; }
    public EfirmaDto? Efirma { get; set; }
    public CiecDto? Ciec { get; set; }

    /// <summary>Cuándo quedó registrado que el cliente autorizó al despacho a resguardarlas.</summary>
    public DateTime? ConsentimientoUtc { get; set; }

    /// <summary>Quien pregunta puede ver contraseñas, descargar, guardar y borrar (además de ver la vigencia).</summary>
    public bool PuedeAdministrar { get; set; }

    /// <summary>Quien pregunta ya tiene NIP (si no, la pantalla le ofrece ponerlo).</summary>
    public bool TieneNip { get; set; }
}

public class EfirmaDto
{
    public string? Rfc { get; set; }
    public string? NumeroDeCertificado { get; set; }
    public DateTime VigenteDesdeUtc { get; set; }
    public DateTime VigenteHastaUtc { get; set; }

    /// <summary>Negativo si ya venció.</summary>
    public int DiasParaVencer { get; set; }

    /// <summary>Vence en <see cref="CredencialesConsts.DiasDeAviso"/> días o menos, o ya venció.</summary>
    public bool PorVencer { get; set; }

    public DateTime ActualizadaUtc { get; set; }
}

public class CiecDto
{
    public string Rfc { get; set; } = "";
    public DateTime ActualizadaUtc { get; set; }
}

/// <summary>Lo que demuestra que es la persona: su NIP de 4 dígitos o la contraseña de su cuenta.</summary>
public class VerificacionDto
{
    [StringLength(4, MinimumLength = 4)]
    [DisableAuditing]
    public string? Nip { get; set; }

    [StringLength(CredencialesConsts.MaxContrasenaLength)]
    [DisableAuditing]
    public string? ContrasenaDeCuenta { get; set; }
}

public class GuardarEfirmaDto
{
    [Required]
    public string CerBase64 { get; set; } = "";

    [Required]
    [DisableAuditing]
    public string KeyBase64 { get; set; } = "";

    [Required]
    [StringLength(CredencialesConsts.MaxContrasenaLength, MinimumLength = 1)]
    [DisableAuditing]
    public string Contrasena { get; set; } = "";

    /// <summary>El cliente autorizó (contrato o carta) que el despacho resguarde sus credenciales.</summary>
    public bool Consentimiento { get; set; }
}

public class GuardarCiecDto
{
    [Required]
    [StringLength(CredencialesConsts.RfcLength, MinimumLength = 12)]
    public string Rfc { get; set; } = "";

    [Required]
    [StringLength(CredencialesConsts.MaxContrasenaLength, MinimumLength = 1)]
    [DisableAuditing]
    public string Contrasena { get; set; } = "";

    public bool Consentimiento { get; set; }
}

public class RevelarCredencialDto : VerificacionDto
{
    public TipoDeCredencial Tipo { get; set; }
}

public class DescargarCredencialDto : VerificacionDto
{
    /// <summary>«cer» o «key».</summary>
    [Required]
    [RegularExpression("^(cer|key)$")]
    public string Archivo { get; set; } = "";
}

public class ContrasenaReveladaDto
{
    [DisableAuditing]
    public string Contrasena { get; set; } = "";
}

public class ArchivoDeCredencialDto
{
    public string Nombre { get; set; } = "";
    public string ContenidoBase64 { get; set; } = "";
}

public class PonerNipDto
{
    [Required]
    [RegularExpression("^[0-9]{4}$", ErrorMessage = "El NIP son 4 dígitos.")]
    [DisableAuditing]
    public string Nip { get; set; } = "";

    [Required]
    [StringLength(CredencialesConsts.MaxContrasenaLength)]
    [DisableAuditing]
    public string ContrasenaDeCuenta { get; set; } = "";
}

public class UsoDeCredencialDto
{
    public DateTime Cuando { get; set; }

    /// <summary>Nombre de quien lo hizo; «Equipo técnico» si es alguien que quien consulta no debe ver.</summary>
    public string Quien { get; set; } = "";

    public TipoDeCredencial Tipo { get; set; }
    public AccionSobreCredencial Accion { get; set; }
}
