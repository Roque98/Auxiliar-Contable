using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace AuxiliarContable.Documentos;

/// <summary>
/// La fecha de emisión de un CFDI: el atributo <c>Fecha</c> del nodo raíz (<c>cfdi:Comprobante</c>).
/// Es lo único que la consola lee de una factura, para acomodarla en su mes; todo lo demás lo lee el
/// motor fiscal. Sin DTD ni entidades externas: el XML lo manda un cliente.
/// </summary>
public static class FechaDelCfdi
{
    private static readonly string[] Formatos = ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd"];

    /// <summary>
    /// El día de emisión, o null si no es un CFDI o no trae una fecha válida. Un CFDI de retenciones se fecha el último día
    /// de su periodo (Periodo MesFin / Ejercicio): la constancia anual de intereses que llega en febrero es del año anterior.
    /// </summary>
    public static DateOnly? Leer(byte[] xml)
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
                return DelPeriodo(lector);
            }

            if (lector.NodeType != XmlNodeType.Element || lector.LocalName != "Comprobante")
            {
                return null;
            }

            var fecha = lector.GetAttribute("Fecha")?.Trim();
            return DateTime.TryParseExact(fecha, Formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor)
                ? DateOnly.FromDateTime(valor)
                : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static DateOnly? DelPeriodo(XmlReader lector)
    {
        while (lector.Read())
        {
            if (lector.NodeType == XmlNodeType.Element && lector.LocalName == "Periodo")
            {
                return int.TryParse(lector.GetAttribute("Ejercicio"), out var anio) && anio is >= 2000 and <= 2100
                       && int.TryParse(lector.GetAttribute("MesFin"), out var mes) && mes is >= 1 and <= 12
                    ? new DateOnly(anio, mes, DateTime.DaysInMonth(anio, mes))
                    : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Cómo se guarda un día en <see cref="Documento.FechaDelDocumento"/>: a mediodía UTC, para que ni
    /// la zona horaria de México ni la de la base lo cambien de día (y de mes).
    /// </summary>
    public static DateTime ComoFecha(DateOnly dia) => new(dia.Year, dia.Month, dia.Day, 12, 0, 0, DateTimeKind.Utc);
}
