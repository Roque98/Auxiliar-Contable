using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuxiliarContable.Facturacion;

/// <summary>Un renglón de lo que se va a facturar.</summary>
public record ConceptoDeFactura(string ClaveProdServ, string Descripcion, decimal Cantidad, string ClaveUnidad,
    decimal PrecioUnitario, decimal? TasaIva)
{
    public decimal Importe => decimal.Round(Cantidad * PrecioUnitario, 2);
    public decimal Iva => TasaIva is { } t ? decimal.Round(Importe * t, 2) : 0m;
}

/// <summary>
/// Lo que el cliente pide que se le facture: a quién (receptor con los datos exactos de su constancia, como pide el
/// CFDI 4.0), qué conceptos, uso, forma y método de pago. El despacho la atiende timbrándola y dejando el XML en la
/// bóveda (<see cref="Atender"/>), o la rechaza diciendo por qué. Subtotal, IVA y total se calculan aquí, no los
/// manda la pantalla.
/// </summary>
public class SolicitudDeFactura : AuditedAggregateRoot<Guid>
{
    public Guid OrganizacionId { get; protected set; }
    public EstadoDeSolicitudDeFactura Estado { get; protected set; }

    public string ReceptorRfc { get; protected set; } = "";
    public string ReceptorNombre { get; protected set; } = "";
    public string ReceptorCodigoPostal { get; protected set; } = "";
    public string ReceptorRegimen { get; protected set; } = "";
    public string UsoCfdi { get; protected set; } = "";
    public string FormaDePago { get; protected set; } = "";
    public string MetodoDePago { get; protected set; } = "";

    /// <summary>Los conceptos en JSON (se guardan y se devuelven enteros).</summary>
    public string Conceptos { get; protected set; } = "[]";

    public decimal Subtotal { get; protected set; }
    public decimal Iva { get; protected set; }
    public decimal Total { get; protected set; }

    public string? NotaDelCliente { get; protected set; }
    public string? Respuesta { get; protected set; }
    public Guid? XmlDocumentoId { get; protected set; }
    public Guid? PdfDocumentoId { get; protected set; }
    public DateTime? AtendidaUtc { get; protected set; }
    public Guid? AtendidaPor { get; protected set; }

    protected SolicitudDeFactura()
    {
    }

    public SolicitudDeFactura(Guid id, Guid organizacionId, string receptorRfc, string receptorNombre, string receptorCodigoPostal,
        string receptorRegimen, string usoCfdi, string formaDePago, string metodoDePago, IReadOnlyList<ConceptoDeFactura> conceptos,
        string? notaDelCliente) : base(id)
    {
        OrganizacionId = organizacionId;
        ReceptorRfc = Check.NotNullOrWhiteSpace(receptorRfc, nameof(receptorRfc), FacturacionConsts.RfcLength).Trim().ToUpperInvariant();
        ReceptorNombre = Check.NotNullOrWhiteSpace(receptorNombre, nameof(receptorNombre), FacturacionConsts.MaxNombreLength).Trim().ToUpperInvariant();
        ReceptorCodigoPostal = Check.NotNullOrWhiteSpace(receptorCodigoPostal, nameof(receptorCodigoPostal), FacturacionConsts.CodigoPostalLength).Trim();
        ReceptorRegimen = Check.NotNullOrWhiteSpace(receptorRegimen, nameof(receptorRegimen), FacturacionConsts.ClaveCortaLength).Trim();
        UsoCfdi = Check.NotNullOrWhiteSpace(usoCfdi, nameof(usoCfdi), FacturacionConsts.ClaveCortaLength).Trim().ToUpperInvariant();
        FormaDePago = Check.NotNullOrWhiteSpace(formaDePago, nameof(formaDePago), FacturacionConsts.ClaveCortaLength).Trim();
        MetodoDePago = Check.NotNullOrWhiteSpace(metodoDePago, nameof(metodoDePago), FacturacionConsts.ClaveCortaLength).Trim().ToUpperInvariant();
        NotaDelCliente = string.IsNullOrWhiteSpace(notaDelCliente) ? null : Check.Length(notaDelCliente.Trim(), nameof(notaDelCliente), FacturacionConsts.MaxNotaLength);
        PonerConceptos(conceptos);
    }

    public IReadOnlyList<ConceptoDeFactura> LeerConceptos() =>
        JsonSerializer.Deserialize<List<ConceptoDeFactura>>(Conceptos) ?? [];

    public void Atender(Guid xmlDocumentoId, Guid? pdfDocumentoId, string? respuesta, Guid? usuarioId, DateTime ahoraUtc)
    {
        ExigirPendiente();
        Estado = EstadoDeSolicitudDeFactura.Emitida;
        XmlDocumentoId = xmlDocumentoId;
        PdfDocumentoId = pdfDocumentoId;
        Responder(respuesta, usuarioId, ahoraUtc);
    }

    public void Rechazar(string respuesta, Guid? usuarioId, DateTime ahoraUtc)
    {
        ExigirPendiente();
        Estado = EstadoDeSolicitudDeFactura.Rechazada;
        Responder(Check.NotNullOrWhiteSpace(respuesta, nameof(respuesta)), usuarioId, ahoraUtc);
    }

    public void Cancelar()
    {
        ExigirPendiente();
        Estado = EstadoDeSolicitudDeFactura.Cancelada;
    }

    private void PonerConceptos(IReadOnlyList<ConceptoDeFactura> conceptos)
    {
        if (conceptos.Count is 0 or > FacturacionConsts.MaxConceptos)
        {
            throw new BusinessException("AuxiliarContable:SolicitudSinConceptos");
        }

        foreach (var c in conceptos)
        {
            if (c.Cantidad <= 0 || c.PrecioUnitario <= 0 || c.ClaveProdServ.Length != 8 || !c.ClaveProdServ.All(char.IsAsciiDigit)
                || string.IsNullOrWhiteSpace(c.Descripcion) || string.IsNullOrWhiteSpace(c.ClaveUnidad)
                || (c.TasaIva is { } t && !FacturacionConsts.TasasDeIva.Contains(t)))
            {
                throw new BusinessException("AuxiliarContable:ConceptoNoValido").WithData("concepto", c.Descripcion);
            }
        }

        Conceptos = JsonSerializer.Serialize(conceptos);
        Subtotal = conceptos.Sum(c => c.Importe);
        Iva = conceptos.Sum(c => c.Iva);
        Total = Subtotal + Iva;
    }

    private void Responder(string? respuesta, Guid? usuarioId, DateTime ahoraUtc)
    {
        if (ahoraUtc.Kind != DateTimeKind.Utc)
        {
            throw new AbpException("AtendidaUtc debe ser UTC (columna timestamptz).");
        }

        Respuesta = string.IsNullOrWhiteSpace(respuesta) ? null : Check.Length(respuesta.Trim(), nameof(respuesta), FacturacionConsts.MaxNotaLength);
        AtendidaPor = usuarioId;
        AtendidaUtc = ahoraUtc;
    }

    private void ExigirPendiente()
    {
        if (Estado != EstadoDeSolicitudDeFactura.Pendiente)
        {
            throw new BusinessException("AuxiliarContable:SolicitudYaAtendida");
        }
    }
}
