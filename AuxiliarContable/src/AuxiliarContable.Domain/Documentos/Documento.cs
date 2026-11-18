using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Documentos;

/// <summary>
/// Un archivo que mandó un cliente (o el despacho por él). El contenido NO vive aquí: va
/// cifrado al disco (<see cref="AlmacenDeDocumentos"/>); aquí quedan los datos para buscarlo,
/// saber de quién es y detectar duplicados.
///
/// <para>⚠️ <see cref="OrganizacionId"/> es la frontera de aislamiento: todo lo que lea
/// documentos filtra por ella con el alcance del usuario (ver <c>AlcanceDeOrganizaciones</c>).</para>
/// </summary>
public class Documento : FullAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }

    public TipoDeDocumento Tipo { get; protected set; }

    /// <summary>El nombre con el que se subió. Solo para mostrarlo: nunca se usa como ruta.</summary>
    public string NombreOriginal { get; protected set; } = string.Empty;

    /// <summary>El tipo REAL, sacado de los bytes (<see cref="DetectorDeDocumento"/>), no el que declaró el navegador.</summary>
    public string TipoMime { get; protected set; } = string.Empty;

    public long Tamano { get; protected set; }

    /// <summary>SHA-256 del contenido en claro: el mismo archivo subido dos veces es el mismo documento.</summary>
    public string Sha256 { get; protected set; } = string.Empty;

    public EstadoDeDocumento Estado { get; protected set; }

    /// <summary>Por qué no se pudo leer, para quien lo revise. Nunca lleva datos del documento.</summary>
    public string? Motivo { get; protected set; }

    /// <summary>
    /// A qué fecha CORRESPONDE (la del CFDI, el corte del estado de cuenta). Null = no se sabe y la
    /// bóveda lo ordena por la fecha en que se subió.
    /// </summary>
    public DateTime? FechaDelDocumento { get; protected set; }

    /// <summary>Lo que Claude leyó del documento (JSON de <c>AnalisisDeDocumento</c>): null = aún no se analiza.</summary>
    public string? Analisis { get; protected set; }

    public DateTime? AnalizadoUtc { get; protected set; }

    /// <summary>Folio fiscal de una factura (de su timbre). Null en lo que no es CFDI, o si el XML no se pudo leer.</summary>
    public string? Uuid { get; protected set; }

    /// <summary>
    /// Una factura cuyo XML no abre o no trae timbre (vacío, la página de error del SAT, cortado): hay que bajarla otra
    /// vez del portal (Descargador 1).
    /// </summary>
    public bool XmlDanado { get; protected set; }

    /// <summary>Lo que dice el portal del SAT de esta factura («Vigente», «Cancelado»); null = no se ha consultado.</summary>
    public string? EstatusSat { get; protected set; }

    public DateTime? EstatusSatUtc { get; protected set; }

    public bool Cancelada => string.Equals(EstatusSat, "Cancelado", StringComparison.OrdinalIgnoreCase);

    protected Documento()
    {
    }

    public Documento(
        Guid id,
        Guid organizacionId,
        TipoDeDocumento tipo,
        string nombreOriginal,
        string tipoMime,
        long tamano,
        string sha256) : base(id)
    {
        OrganizacionId = organizacionId;
        Tipo = tipo;
        NombreOriginal = Check.NotNullOrWhiteSpace(nombreOriginal, nameof(nombreOriginal), DocumentoConsts.MaxNombreLength);
        TipoMime = Check.NotNullOrWhiteSpace(tipoMime, nameof(tipoMime), DocumentoConsts.MaxTipoMimeLength);
        Tamano = tamano;
        Sha256 = Check.Length(sha256, nameof(sha256), DocumentoConsts.HashLength, DocumentoConsts.HashLength)!;
        Estado = EstadoDeDocumento.Recibido;
    }

    public void FecharEn(DateTime fecha) => FechaDelDocumento = fecha;

    public void GuardarAnalisis(string json, DateTime utc)
    {
        Analisis = Check.NotNullOrWhiteSpace(json, nameof(json));
        AnalizadoUtc = utc;
    }

    /// <summary>El UUID leído del XML; null = el XML está dañado.</summary>
    public void Identificar(string? uuid)
    {
        var limpio = uuid?.Trim().ToUpperInvariant();
        if (limpio is { Length: DocumentoConsts.UuidLength })
        {
            Uuid = limpio;
            XmlDanado = false;
        }
        else
        {
            Uuid = null;
            XmlDanado = true;
        }
    }

    public void PonerEstatusSat(string estatus, DateTime utc)
    {
        var limpio = Check.NotNullOrWhiteSpace(estatus, nameof(estatus)).Trim();
        EstatusSat = limpio.Length <= DocumentoConsts.MaxEstatusSatLength ? limpio : limpio[..DocumentoConsts.MaxEstatusSatLength];
        EstatusSatUtc = utc;
    }

    public void MarcarProcesado()
    {
        Estado = EstadoDeDocumento.Procesado;
        Motivo = null;
    }

    public void MarcarConError(string motivo)
    {
        Estado = EstadoDeDocumento.ConError;
        Motivo = motivo.Length <= 500 ? motivo : motivo[..500];
    }
}
