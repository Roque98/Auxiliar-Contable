using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace AuxiliarContable.Documentos;

/// <summary>
/// Lo poco de un CFDI que hace falta para empezar su operación: folio fiscal, fecha, total, emisor, receptor y el
/// primer concepto. También lee los CFDI de Retenciones 2.0 (raíz <c>Retenciones</c>): <see cref="Tipo"/> «R», su
/// periodo como fecha y el monto de la operación como total. Lo fiscal de fondo lo sigue leyendo el motor. Sin DTD ni
/// entidades externas: el XML lo manda un cliente.
/// </summary>
public sealed record DatosDelCfdi(
    string? Uuid,
    DateOnly? Fecha,
    decimal? Total,
    string? Tipo,
    string? EmisorRfc,
    string? EmisorNombre,
    string? ReceptorRfc,
    string? ReceptorNombre,
    string? PrimerConcepto)
{
    /// <summary>«R»: un CFDI de retenciones e información de pagos (no se verifica como factura ni abre operación).</summary>
    public const string TipoRetenciones = "R";

    public bool EsRetencion => Tipo == TipoRetenciones;

    /// <summary>PUE o PPD.</summary>
    public string? MetodoPago { get; init; }

    /// <summary>Clave del catálogo c_FormaPago (01 efectivo, 03 transferencia, 12 dación en pago, 99 por definir…).</summary>
    public string? FormaPago { get; init; }

    public string? Moneda { get; init; }

    /// <summary>De CfdiRelacionados: 01 nota de crédito, 03 devolución, 07 aplicación de anticipo…</summary>
    public string? TipoRelacion { get; init; }

    /// <summary>El primer CFDI relacionado (la factura a la que corresponde una nota de crédito, p. ej.).</summary>
    public string? UuidRelacionado { get; init; }

    /// <summary>El atributo Total tal como viene en el XML (lo pide así la verificación del SAT); null si no lo trae.</summary>
    public static string? TotalTalCual(byte[] xml)
    {
        try
        {
            var opciones = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var lector = XmlReader.Create(new MemoryStream(xml), opciones);
            return lector.MoveToContent() == XmlNodeType.Element && lector.LocalName == "Comprobante"
                ? lector.GetAttribute("Total")?.Trim()
                : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Null si no es un CFDI.</summary>
    public static DatosDelCfdi? Leer(byte[] xml)
    {
        try
        {
            var opciones = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };
            using var lector = XmlReader.Create(new MemoryStream(xml), opciones);
            if (lector.MoveToContent() == XmlNodeType.Element && lector.LocalName == "Retenciones")
            {
                return LeerRetencion(lector, FechaDelCfdi.Leer(xml));
            }

            if (lector.NodeType != XmlNodeType.Element || lector.LocalName != "Comprobante")
            {
                return null;
            }

            var fecha = FechaDelCfdi.Leer(xml);
            decimal? total = decimal.TryParse(lector.GetAttribute("Total"), NumberStyles.Number, CultureInfo.InvariantCulture, out var t) ? t : null;
            var tipo = lector.GetAttribute("TipoDeComprobante");
            var metodo = lector.GetAttribute("MetodoPago")?.Trim().ToUpperInvariant();
            var forma = lector.GetAttribute("FormaPago")?.Trim();
            var moneda = lector.GetAttribute("Moneda")?.Trim().ToUpperInvariant();
            string? tipoRelacion = null, relacionado = null;
            string? uuid = null, emisorRfc = null, emisorNombre = null, receptorRfc = null, receptorNombre = null, concepto = null;
            while (lector.Read())
            {
                if (lector.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                switch (lector.LocalName)
                {
                    case "Emisor" when emisorRfc is null:
                        emisorRfc = lector.GetAttribute("Rfc")?.Trim().ToUpperInvariant();
                        emisorNombre = lector.GetAttribute("Nombre")?.Trim();
                        break;
                    case "Receptor" when receptorRfc is null:
                        receptorRfc = lector.GetAttribute("Rfc")?.Trim().ToUpperInvariant();
                        receptorNombre = lector.GetAttribute("Nombre")?.Trim();
                        break;
                    case "Concepto" when concepto is null:
                        concepto = lector.GetAttribute("Descripcion")?.Trim();
                        break;
                    case "TimbreFiscalDigital":
                        uuid = lector.GetAttribute("UUID")?.Trim().ToUpperInvariant();
                        break;
                    case "CfdiRelacionados" when tipoRelacion is null:
                        tipoRelacion = lector.GetAttribute("TipoRelacion")?.Trim();
                        break;
                    case "CfdiRelacionado" when relacionado is null:
                        relacionado = lector.GetAttribute("UUID")?.Trim().ToUpperInvariant();
                        break;
                }
            }

            return new DatosDelCfdi(uuid, fecha, total, tipo, emisorRfc, emisorNombre, receptorRfc, receptorNombre, concepto)
            {
                MetodoPago = string.IsNullOrWhiteSpace(metodo) ? null : metodo,
                FormaPago = string.IsNullOrWhiteSpace(forma) ? null : forma,
                Moneda = string.IsNullOrWhiteSpace(moneda) ? null : moneda,
                TipoRelacion = string.IsNullOrWhiteSpace(tipoRelacion) ? null : tipoRelacion,
                UuidRelacionado = string.IsNullOrWhiteSpace(relacionado) ? null : relacionado,
            };
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Retenciones 2.0 (retencionpagov2.xsd): Emisor RfcE, Receptor/Nacional RfcR, Totales MontoTotOperacion.</summary>
    private static DatosDelCfdi LeerRetencion(XmlReader lector, DateOnly? fecha)
    {
        var descripcion = lector.GetAttribute("DescRetenc")?.Trim();
        string? uuid = null, emisorRfc = null, emisorNombre = null, receptorRfc = null, receptorNombre = null;
        decimal? total = null;
        while (lector.Read())
        {
            if (lector.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (lector.LocalName)
            {
                case "Emisor" when emisorRfc is null:
                    emisorRfc = lector.GetAttribute("RfcE")?.Trim().ToUpperInvariant();
                    emisorNombre = lector.GetAttribute("NomDenRazSocE")?.Trim();
                    break;
                case "Nacional" when receptorRfc is null:
                    receptorRfc = lector.GetAttribute("RfcR")?.Trim().ToUpperInvariant();
                    receptorNombre = lector.GetAttribute("NomDenRazSocR")?.Trim();
                    break;
                case "Totales" when total is null:
                    total = decimal.TryParse(lector.GetAttribute("MontoTotOperacion"), NumberStyles.Number, CultureInfo.InvariantCulture, out var t) ? t : null;
                    break;
                case "TimbreFiscalDigital":
                    uuid = lector.GetAttribute("UUID")?.Trim().ToUpperInvariant();
                    break;
            }
        }

        return new DatosDelCfdi(uuid, fecha, total, TipoRetenciones, emisorRfc, emisorNombre, receptorRfc, receptorNombre,
            string.IsNullOrWhiteSpace(descripcion) ? "CFDI de retenciones" : $"Retenciones: {descripcion}");
    }
}
