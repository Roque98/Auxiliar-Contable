using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuxiliarContable.Documentos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AuxiliarContable.Boveda;

/// <summary>
/// La bóveda de documentos de un cliente: subir (CFDI en XML, estados de cuenta, tickets),
/// navegar por año → mes → tipo, buscar y descargar. Ver = <c>Ver</c>; subir y borrar = <c>Subir</c>.
/// Aquí solo se guardan y se ordenan: leerlos (CFDI, estados de cuenta) es trabajo del motor
/// fiscal (<c>motores-fiscales</c>), no de esta aplicación.
/// </summary>
public interface IBovedaAppService : IApplicationService
{
    /// <summary>
    /// Un archivo por llamada. Se guarda cifrado; no se interpreta su contenido, salvo la fecha de una
    /// factura (la de su XML). Lo demás va al mes que diga quien lo sube (<paramref name="anio"/> y
    /// <paramref name="mes"/>; sin ellos, al mes en que se sube): un estado de cuenta de octubre llega en
    /// noviembre y se concilia en octubre.
    /// </summary>
    Task<ResultadoDeSubidaDto> SubirAsync(Guid organizacionId, TipoDeDocumento tipo, IRemoteStreamContent archivo, int? anio = null, int? mes = null);

    /// <summary>Mueve un documento a otro mes (el que se subió en el mes equivocado). Las facturas no: su mes es el de su XML.</summary>
    Task<DocumentoDeBovedaDto> CambiarMesAsync(Guid organizacionId, Guid documentoId, int anio, int mes);

    Task<PagedResultDto<DocumentoDeBovedaDto>> GetListAsync(BuscarEnBovedaDto input);

    /// <summary>Cuántos documentos hay por año, mes y tipo: la navegación.</summary>
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

    /// <summary>Parte del nombre del archivo.</summary>
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

    /// <summary>A qué fecha corresponde el documento; si no se sabe, la de subida.</summary>
    public DateTime Fecha { get; set; }
}

public class ResultadoDeSubidaDto
{
    public DocumentoDeBovedaDto Documento { get; set; } = null!;

    /// <summary>Ya estaba (mismo archivo): no se guardó otra vez.</summary>
    public bool EsDuplicado { get; set; }

    /// <summary>Algo que la persona debe saber sobre lo que subió.</summary>
    public string? Aviso { get; set; }
}

public class ResumenDelMesDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public TipoDeDocumento Tipo { get; set; }
    public int Cantidad { get; set; }
}
