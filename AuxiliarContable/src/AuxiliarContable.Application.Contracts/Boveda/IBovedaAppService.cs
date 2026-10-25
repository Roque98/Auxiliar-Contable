using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.Cfdis;
using AuxiliarContable.Documentos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuxiliarContable.Boveda;

/// <summary>
/// La bóveda de documentos de un cliente: subir (CFDI en XML, estados de cuenta, tickets),
/// navegar por año → mes → tipo, buscar y descargar. Ver = <c>Ver</c>; subir y borrar = <c>Subir</c>.
/// </summary>
public interface IBovedaAppService : IApplicationService
{
    /// <summary>Un archivo por llamada. Un XML se lee como CFDI al momento (por código, sin IA).</summary>
    Task<ResultadoDeSubidaDto> SubirAsync(Guid organizacionId, TipoDeDocumento tipo, IRemoteStreamContent archivo);

    Task<PagedResultDto<DocumentoDeBovedaDto>> GetListAsync(BuscarEnBovedaDto input);

    /// <summary>Cuántos documentos hay por año, mes y tipo, y los totales de CFDI: la navegación.</summary>
    Task<List<ResumenDelMesDto>> GetResumenAsync(Guid organizacionId);

    /// <summary>El archivo original, descifrado. Solo por la API autenticada: no hay enlaces públicos.</summary>
    Task<IRemoteStreamContent> GetArchivoAsync(Guid organizacionId, Guid documentoId);

    Task EliminarAsync(Guid organizacionId, Guid documentoId);
}

public class BuscarEnBovedaDto : PagedResultRequestDto
{
    public Guid OrganizacionId { get; set; }
    public int? Anio { get; set; }
    public int? Mes { get; set; }
    public TipoDeDocumento? Tipo { get; set; }
    public DireccionDelCfdi? Direccion { get; set; }

    /// <summary>Nombre del archivo, RFC o nombre de emisor/receptor, UUID o concepto.</summary>
    public string? Texto { get; set; }
}

public class DocumentoDeBovedaDto
{
    public Guid Id { get; set; }
    public TipoDeDocumento Tipo { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string TipoMime { get; set; } = string.Empty;
    public long Tamano { get; set; }
    public EstadoDeDocumento Estado { get; set; }
    public string? Motivo { get; set; }
    public DateTime SubidoUtc { get; set; }

    /// <summary>A qué fecha corresponde (la del CFDI); si no se sabe, la de subida.</summary>
    public DateTime Fecha { get; set; }

    public CfdiResumenDto? Cfdi { get; set; }
}

public class CfdiResumenDto
{
    public string Uuid { get; set; } = string.Empty;
    public string TipoDeComprobante { get; set; } = string.Empty;
    public DireccionDelCfdi Direccion { get; set; }
    public string? Serie { get; set; }
    public string? Folio { get; set; }
    public string EmisorRfc { get; set; } = string.Empty;
    public string? EmisorNombre { get; set; }
    public string ReceptorRfc { get; set; } = string.Empty;
    public string? ReceptorNombre { get; set; }
    public decimal Total { get; set; }
    public string Moneda { get; set; } = "MXN";
    public string? MetodoDePago { get; set; }
    public string? Concepto { get; set; }
}

public class ResultadoDeSubidaDto
{
    public DocumentoDeBovedaDto Documento { get; set; } = null!;

    /// <summary>Ya estaba (mismo archivo o, en un CFDI, mismo UUID): no se guardó otra vez.</summary>
    public bool EsDuplicado { get; set; }

    /// <summary>Algo que la persona debe saber (p. ej. un CFDI que no es ni del ni para el cliente).</summary>
    public string? Aviso { get; set; }
}

public class ResumenDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public TipoDeDocumento Tipo { get; set; }
    public int Cantidad { get; set; }

    /// <summary>Solo CFDI en MXN: suma de los emitidos (ingresos) y de los recibidos (gastos). Informativo, no fiscal.</summary>
    public decimal TotalEmitido { get; set; }

    public decimal TotalRecibido { get; set; }
}
