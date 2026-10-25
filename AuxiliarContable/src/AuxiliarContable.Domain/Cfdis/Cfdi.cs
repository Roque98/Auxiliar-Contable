using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Cfdis;

/// <summary>
/// Un CFDI del cliente, leído de su XML (<see cref="LectorDeCfdi"/>). El archivo vive en
/// <see cref="Documentos.Documento"/> (cifrado); aquí, los datos para filtrar, buscar y,
/// después, conciliar contra el banco. El UUID no se repite dentro de un cliente.
///
/// <para>Emitido o recibido NO se guarda: depende del RFC del perfil, que puede llegar o
/// corregirse después. Se calcula al consultar (<see cref="Direccion"/>).</para>
/// </summary>
public class Cfdi : FullAuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public Guid DocumentoId { get; protected set; }
    public string Uuid { get; protected set; } = string.Empty;
    public string Version { get; protected set; } = string.Empty;
    public DateTime Fecha { get; protected set; }
    public string TipoDeComprobante { get; protected set; } = "I";
    public string? Serie { get; protected set; }
    public string? Folio { get; protected set; }
    public string EmisorRfc { get; protected set; } = string.Empty;
    public string? EmisorNombre { get; protected set; }
    public string ReceptorRfc { get; protected set; } = string.Empty;
    public string? ReceptorNombre { get; protected set; }
    public string? UsoCfdi { get; protected set; }
    public decimal SubTotal { get; protected set; }
    public decimal Descuento { get; protected set; }
    public decimal Total { get; protected set; }
    public string Moneda { get; protected set; } = "MXN";
    public decimal? TipoDeCambio { get; protected set; }
    public string? MetodoDePago { get; protected set; }
    public string? FormaDePago { get; protected set; }
    public decimal TotalTrasladados { get; protected set; }
    public decimal TotalRetenidos { get; protected set; }
    public string? Concepto { get; protected set; }

    protected Cfdi()
    {
    }

    public Cfdi(Guid id, Guid organizacionId, Guid documentoId, DatosDeCfdi datos) : base(id)
    {
        OrganizacionId = organizacionId;
        DocumentoId = documentoId;
        Uuid = datos.Uuid;
        Version = datos.Version;
        Fecha = datos.Fecha;
        TipoDeComprobante = datos.TipoDeComprobante;
        Serie = Recortar(datos.Serie, 50);
        Folio = Recortar(datos.Folio, 50);
        EmisorRfc = datos.EmisorRfc;
        EmisorNombre = Recortar(datos.EmisorNombre, CfdiConsts.MaxNombreLength);
        ReceptorRfc = datos.ReceptorRfc;
        ReceptorNombre = Recortar(datos.ReceptorNombre, CfdiConsts.MaxNombreLength);
        UsoCfdi = datos.UsoCfdi;
        SubTotal = datos.SubTotal;
        Descuento = datos.Descuento;
        Total = datos.Total;
        Moneda = datos.Moneda;
        TipoDeCambio = datos.TipoDeCambio;
        MetodoDePago = datos.MetodoDePago;
        FormaDePago = datos.FormaDePago;
        TotalTrasladados = datos.TotalTrasladados;
        TotalRetenidos = datos.TotalRetenidos;
        Concepto = Recortar(datos.PrimerConcepto, CfdiConsts.MaxConceptoLength);
    }

    public DireccionDelCfdi Direccion(string? rfcDelCliente) => DireccionPara(EmisorRfc, ReceptorRfc, rfcDelCliente);

    public static DireccionDelCfdi DireccionPara(string emisorRfc, string receptorRfc, string? rfcDelCliente)
    {
        var rfc = rfcDelCliente?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(rfc) ? DireccionDelCfdi.Desconocida
            : rfc == emisorRfc ? DireccionDelCfdi.Emitido
            : rfc == receptorRfc ? DireccionDelCfdi.Recibido
            : DireccionDelCfdi.Desconocida;
    }

    private static string? Recortar(string? texto, int max) => texto is null || texto.Length <= max ? texto : texto[..max];
}
