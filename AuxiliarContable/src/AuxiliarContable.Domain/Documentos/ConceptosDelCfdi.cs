using System.Collections.Generic;
using System.IO;
using System.Xml;
using AuxiliarContable.MotorFiscal;

namespace AuxiliarContable.Documentos;

/// <summary>
/// Los conceptos de un CFDI (clave SAT, unidad y descripción) para revisar si la clave corresponde a lo que se vendió o
/// compró. Solo los del comprobante, no los de complementos. Sin DTD ni entidades externas: el XML lo manda un cliente.
/// </summary>
public static class ConceptosDelCfdi
{
    public const int Max = 60;

    public static List<ConceptoParaRevisar> Leer(byte[] xml)
    {
        var fuera = new List<ConceptoParaRevisar>();
        try
        {
            var opciones = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
            using var lector = XmlReader.Create(new MemoryStream(xml), opciones);
            if (lector.MoveToContent() != XmlNodeType.Element || lector.LocalName != "Comprobante")
            {
                return fuera;
            }

            while (lector.Read() && fuera.Count < Max)
            {
                // Concepto directo de Conceptos (los «Parte» y los de complementos tienen otro nombre o están más abajo).
                if (lector.NodeType == XmlNodeType.Element && lector.LocalName == "Concepto" && lector.Depth == 2)
                {
                    fuera.Add(new ConceptoParaRevisar(
                        lector.GetAttribute("ClaveProdServ")?.Trim() ?? "",
                        lector.GetAttribute("ClaveUnidad")?.Trim() ?? "",
                        lector.GetAttribute("Descripcion")?.Trim() ?? ""));
                }
            }
        }
        catch (XmlException)
        {
            // Un XML roto no tiene conceptos que revisar.
        }

        return fuera;
    }
}
