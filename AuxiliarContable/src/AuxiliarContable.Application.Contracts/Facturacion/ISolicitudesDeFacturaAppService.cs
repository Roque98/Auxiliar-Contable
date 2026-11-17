using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Facturacion;

/// <summary>
/// Pedir factura: el cliente (con permiso de subir) pide que el despacho le timbre una factura; el despacho la atiende
/// (deja el XML y el PDF en la bóveda) o la rechaza. Por ahora el timbrado lo hace el despacho en su sistema; se
/// conecta un PAC cuando se acuerden los precios del plan de facturación.
/// </summary>
public interface ISolicitudesDeFacturaAppService : IApplicationService
{
    /// <summary>Las del cliente, de la más nueva a la más vieja.</summary>
    Task<SolicitudesDelClienteDto> GetListAsync(Guid organizacionId);

    /// <summary>Solo el equipo: las pendientes de todos los clientes, de la más vieja a la más nueva.</summary>
    Task<List<SolicitudDeFacturaDto>> GetPendientesAsync();

    Task<SolicitudDeFacturaDto> CrearAsync(Guid organizacionId, CrearSolicitudDeFacturaDto input);

    Task<SolicitudDeFacturaDto> CancelarAsync(Guid organizacionId, Guid solicitudId);

    /// <summary>Solo el equipo: ya se timbró; el XML (y el PDF) son documentos de la bóveda del cliente.</summary>
    Task<SolicitudDeFacturaDto> AtenderAsync(Guid organizacionId, Guid solicitudId, AtenderSolicitudDto input);

    /// <summary>Solo el equipo: no se pudo emitir.</summary>
    Task<SolicitudDeFacturaDto> RechazarAsync(Guid organizacionId, Guid solicitudId, RechazarSolicitudDto input);

    /// <summary>Lee la Constancia de Situación Fiscal del receptor (PDF en base64) para llenar sus datos.</summary>
    Task<ReceptorDto> LeerConstanciaDelReceptorAsync(Guid organizacionId, LeerConstanciaDelReceptorDto input);
}

public class ConceptoDto
{
    [Required]
    [RegularExpression("^[0-9]{8}$", ErrorMessage = "La clave del SAT son 8 dígitos.")]
    public string ClaveProdServ { get; set; } = "";

    [Required]
    [StringLength(FacturacionConsts.MaxDescripcionLength, MinimumLength = 1)]
    public string Descripcion { get; set; } = "";

    [Range(0.000001, 999_999_999)]
    public decimal Cantidad { get; set; } = 1;

    [Required]
    [StringLength(FacturacionConsts.ClaveCortaLength, MinimumLength = 2)]
    public string ClaveUnidad { get; set; } = "";

    /// <summary>Sin IVA.</summary>
    [Range(0.000001, 999_999_999)]
    public decimal PrecioUnitario { get; set; }

    /// <summary>0.16, 0.08, 0 o null (exento).</summary>
    public decimal? TasaIva { get; set; } = 0.16m;

    public decimal Importe { get; set; }
    public decimal Iva { get; set; }
}

public class ReceptorDto
{
    [Required]
    [StringLength(FacturacionConsts.RfcLength, MinimumLength = 12)]
    public string Rfc { get; set; } = "";

    [Required]
    [StringLength(FacturacionConsts.MaxNombreLength, MinimumLength = 1)]
    public string Nombre { get; set; } = "";

    [Required]
    [RegularExpression("^[0-9]{5}$", ErrorMessage = "El código postal son 5 dígitos.")]
    public string CodigoPostal { get; set; } = "";

    [Required]
    [RegularExpression("^[0-9]{3}$", ErrorMessage = "El régimen fiscal es la clave de 3 dígitos (p. ej. 601, 612, 626).")]
    public string Regimen { get; set; } = "";
}

public class CrearSolicitudDeFacturaDto
{
    [Required]
    public ReceptorDto Receptor { get; set; } = new();

    [Required]
    [StringLength(FacturacionConsts.ClaveCortaLength, MinimumLength = 3)]
    public string UsoCfdi { get; set; } = "G03";

    [Required]
    [RegularExpression("^[0-9]{2}$")]
    public string FormaDePago { get; set; } = "03";

    [Required]
    [RegularExpression("^(PUE|PPD)$")]
    public string MetodoDePago { get; set; } = "PUE";

    [Required]
    public List<ConceptoDto> Conceptos { get; set; } = [];

    [StringLength(FacturacionConsts.MaxNotaLength)]
    public string? Nota { get; set; }
}

public class SolicitudDeFacturaDto
{
    public Guid Id { get; set; }
    public Guid OrganizacionId { get; set; }

    /// <summary>Nombre del cliente (en la cola del despacho).</summary>
    public string? Cliente { get; set; }

    public EstadoDeSolicitudDeFactura Estado { get; set; }
    public DateTime PedidaUtc { get; set; }
    public ReceptorDto Receptor { get; set; } = new();
    public string UsoCfdi { get; set; } = "";
    public string FormaDePago { get; set; } = "";
    public string MetodoDePago { get; set; } = "";
    public List<ConceptoDto> Conceptos { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public string? Nota { get; set; }
    public string? Respuesta { get; set; }
    public DateTime? AtendidaUtc { get; set; }
    public Guid? XmlDocumentoId { get; set; }
    public string? NombreDelXml { get; set; }
    public Guid? PdfDocumentoId { get; set; }
    public string? NombreDelPdf { get; set; }
}

public class SolicitudesDelClienteDto
{
    public List<SolicitudDeFacturaDto> Solicitudes { get; set; } = [];

    /// <summary>A quiénes ya les pidió factura: para elegirlos sin volver a capturarlos.</summary>
    public List<ReceptorDto> Receptores { get; set; } = [];

    public bool PuedePedir { get; set; }

    /// <summary>Quien pregunta es del despacho: puede atender y rechazar.</summary>
    public bool EsEquipo { get; set; }
}

public class AtenderSolicitudDto
{
    public Guid XmlDocumentoId { get; set; }
    public Guid? PdfDocumentoId { get; set; }

    [StringLength(FacturacionConsts.MaxNotaLength)]
    public string? Respuesta { get; set; }
}

public class RechazarSolicitudDto
{
    [Required]
    [StringLength(FacturacionConsts.MaxNotaLength, MinimumLength = 1)]
    public string Respuesta { get; set; } = "";
}

public class LeerConstanciaDelReceptorDto
{
    [Required]
    public string PdfBase64 { get; set; } = "";
}
