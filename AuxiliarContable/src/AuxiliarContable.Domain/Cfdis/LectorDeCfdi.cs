using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace AuxiliarContable.Cfdis;

/// <summary>
/// Lee un CFDI (3.3 o 4.0) POR CÓDIGO: el XML ya trae los datos estructurados y timbrados por el
/// SAT, así que aquí no entra la IA (CLAUDE.md §1.2.8: cálculos y datos exactos = código).
///
/// <para>⚠️ XML hostil: sin DTD (<see cref="DtdProcessing.Prohibit"/>), sin resolver externos y
/// con tope de tamaño. <see cref="Documentos.DetectorDeDocumento"/> ya rechaza <c>&lt;!DOCTYPE</c>;
/// esto es la segunda barrera.</para>
/// </summary>
public static class LectorDeCfdi
{
    private static readonly XmlReaderSettings Ajustes = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 30_000_000,
        IgnoreComments = true,
    };

    /// <summary>Los datos del comprobante, o <see cref="CfdiInvalidoException"/> con el motivo en palabras de persona.</summary>
    public static DatosDeCfdi Leer(byte[] xml)
    {
        XDocument documento;
        try
        {
            using var lector = XmlReader.Create(new MemoryStream(xml), Ajustes);
            documento = XDocument.Load(lector);
        }
        catch (XmlException ex)
        {
            throw new CfdiInvalidoException($"El archivo no es un XML válido ({ex.Message}).");
        }

        var raiz = documento.Root;
        if (raiz is null || raiz.Name.LocalName != "Comprobante" || !raiz.Name.NamespaceName.StartsWith("http://www.sat.gob.mx/cfd/", StringComparison.Ordinal))
        {
            throw new CfdiInvalidoException("El XML no es un CFDI (no tiene el nodo «Comprobante» del SAT).");
        }

        var version = Atributo(raiz, "Version") ?? Atributo(raiz, "version") ?? string.Empty;
        var timbre = raiz.Descendants().FirstOrDefault(e => e.Name.LocalName == "TimbreFiscalDigital");
        var uuid = timbre is null ? null : Atributo(timbre, "UUID");
        if (string.IsNullOrWhiteSpace(uuid) || !Guid.TryParse(uuid, out _))
        {
            throw new CfdiInvalidoException("El CFDI no está timbrado (no trae el folio fiscal / UUID).");
        }

        var emisor = Hijo(raiz, "Emisor") ?? throw new CfdiInvalidoException("Al CFDI le falta el emisor.");
        var receptor = Hijo(raiz, "Receptor") ?? throw new CfdiInvalidoException("Al CFDI le falta el receptor.");
        var impuestos = Hijo(raiz, "Impuestos");

        var fechaTexto = Atributo(raiz, "Fecha") ?? throw new CfdiInvalidoException("Al CFDI le falta la fecha.");
        if (!DateTime.TryParseExact(fechaTexto, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            throw new CfdiInvalidoException($"La fecha del CFDI («{fechaTexto}») no tiene el formato del SAT.");
        }

        DateTime? fechaTimbrado = DateTime.TryParseExact(Atributo(timbre!, "FechaTimbrado"), "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var ft) ? ft : null;

        return new DatosDeCfdi
        {
            Version = version,
            Uuid = uuid.ToUpperInvariant(),
            Fecha = fecha,
            FechaTimbrado = fechaTimbrado,
            Serie = Atributo(raiz, "Serie"),
            Folio = Atributo(raiz, "Folio"),
            TipoDeComprobante = (Atributo(raiz, "TipoDeComprobante") ?? "I").ToUpperInvariant(),
            EmisorRfc = (Atributo(emisor, "Rfc") ?? string.Empty).ToUpperInvariant(),
            EmisorNombre = Atributo(emisor, "Nombre"),
            EmisorRegimen = Atributo(emisor, "RegimenFiscal"),
            ReceptorRfc = (Atributo(receptor, "Rfc") ?? string.Empty).ToUpperInvariant(),
            ReceptorNombre = Atributo(receptor, "Nombre"),
            UsoCfdi = Atributo(receptor, "UsoCFDI"),
            SubTotal = Importe(raiz, "SubTotal") ?? 0m,
            Descuento = Importe(raiz, "Descuento") ?? 0m,
            Total = Importe(raiz, "Total") ?? throw new CfdiInvalidoException("Al CFDI le falta el total."),
            Moneda = Atributo(raiz, "Moneda") ?? "MXN",
            TipoDeCambio = Importe(raiz, "TipoCambio"),
            MetodoDePago = Atributo(raiz, "MetodoPago"),
            FormaDePago = Atributo(raiz, "FormaPago"),
            TotalTrasladados = impuestos is null ? 0m : Importe(impuestos, "TotalImpuestosTrasladados") ?? 0m,
            TotalRetenidos = impuestos is null ? 0m : Importe(impuestos, "TotalImpuestosRetenidos") ?? 0m,
            Conceptos = Hijo(raiz, "Conceptos")?.Elements().Count(e => e.Name.LocalName == "Concepto") ?? 0,
            PrimerConcepto = Hijo(raiz, "Conceptos")?.Elements().FirstOrDefault(e => e.Name.LocalName == "Concepto") is { } c
                ? Atributo(c, "Descripcion")
                : null,
        };
    }

    private static XElement? Hijo(XElement padre, string nombre) => padre.Elements().FirstOrDefault(e => e.Name.LocalName == nombre);

    private static string? Atributo(XElement elemento, string nombre)
    {
        var valor = elemento.Attributes().FirstOrDefault(a => a.Name.LocalName == nombre)?.Value?.Trim();
        return string.IsNullOrEmpty(valor) ? null : valor;
    }

    private static decimal? Importe(XElement elemento, string nombre)
    {
        var texto = Atributo(elemento, nombre);
        if (texto is null)
        {
            return null;
        }

        return decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new CfdiInvalidoException($"El importe «{nombre}» del CFDI no es un número ({texto}).");
    }
}

public sealed class DatosDeCfdi
{
    public string Version { get; init; } = string.Empty;
    public string Uuid { get; init; } = string.Empty;
    public DateTime Fecha { get; init; }
    public DateTime? FechaTimbrado { get; init; }
    public string? Serie { get; init; }
    public string? Folio { get; init; }

    /// <summary>I ingreso, E egreso, T traslado, N nómina, P pago.</summary>
    public string TipoDeComprobante { get; init; } = "I";

    public string EmisorRfc { get; init; } = string.Empty;
    public string? EmisorNombre { get; init; }
    public string? EmisorRegimen { get; init; }
    public string ReceptorRfc { get; init; } = string.Empty;
    public string? ReceptorNombre { get; init; }
    public string? UsoCfdi { get; init; }
    public decimal SubTotal { get; init; }
    public decimal Descuento { get; init; }
    public decimal Total { get; init; }
    public string Moneda { get; init; } = "MXN";
    public decimal? TipoDeCambio { get; init; }
    public string? MetodoDePago { get; init; }
    public string? FormaDePago { get; init; }
    public decimal TotalTrasladados { get; init; }
    public decimal TotalRetenidos { get; init; }
    public int Conceptos { get; init; }
    public string? PrimerConcepto { get; init; }
}

public class CfdiInvalidoException : Exception
{
    public CfdiInvalidoException(string motivo) : base(motivo)
    {
    }
}
