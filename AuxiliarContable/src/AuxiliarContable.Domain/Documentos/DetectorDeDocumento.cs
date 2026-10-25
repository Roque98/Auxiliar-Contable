using System;
using AuxiliarContable.Files;

namespace AuxiliarContable.Documentos;

/// <summary>
/// El tipo de un archivo MIRANDO SUS BYTES, como <see cref="DetectorDeImagen"/>: el
/// <c>Content-Type</c> y la extensión los elige quien sube y pueden mentir. Lo que no se
/// reconoce se rechaza; así nunca se guarda (ni se sirve después) un HTML o un ejecutable
/// disfrazado.
/// </summary>
public static class DetectorDeDocumento
{
    public const string Pdf = "application/pdf";
    public const string Xml = "application/xml";

    private static readonly byte[] FirmaPdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>El tipo MIME real, o null si no es un formato que se acepte.</summary>
    public static string? Detectar(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(FirmaPdf))
        {
            return Pdf;
        }

        if (EsXml(bytes))
        {
            return Xml;
        }

        return DetectorDeImagen.Detectar(bytes) switch
        {
            FormatoDeImagen.Jpeg => "image/jpeg",
            FormatoDeImagen.Png => "image/png",
            FormatoDeImagen.WebP => "image/webp",
            _ => null,
        };
    }

    /// <summary>
    /// Un CFDI es XML: empieza (tras un BOM o espacios) por <c>&lt;?xml</c> o por un elemento.
    /// Se exige además que NO contenga <c>&lt;!DOCTYPE</c>: un CFDI nunca lo trae, y es la puerta
    /// de las entidades externas (XXE) y de las «bombas» de expansión.
    /// </summary>
    private static bool EsXml(ReadOnlySpan<byte> bytes)
    {
        var resto = bytes.StartsWith(Bom) ? bytes[Bom.Length..] : bytes;
        var i = 0;
        while (i < resto.Length && (resto[i] == ' ' || resto[i] == '\t' || resto[i] == '\r' || resto[i] == '\n'))
        {
            i++;
        }

        if (i >= resto.Length || resto[i] != '<')
        {
            return false;
        }

        return resto.IndexOf("<!DOCTYPE"u8) < 0;
    }
}
