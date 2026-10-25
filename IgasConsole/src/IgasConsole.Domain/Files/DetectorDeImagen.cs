using System;

namespace IgasConsole.Files;

public enum FormatoDeImagen
{
    Desconocido = 0,
    Jpeg = 1,
    Png = 2,
    WebP = 3
}

/// <summary>
/// Averigua el formato de una imagen MIRANDO SUS BYTES, no el <c>Content-Type</c> que
/// declara quien sube el archivo.
/// <para>Esa distinción es la que hace segura la funcionalidad: el <c>Content-Type</c> lo
/// elige el cliente y puede mentir. Alguien podría subir un HTML o un SVG con
/// <c>&lt;script&gt;</c> diciendo que es <c>image/png</c>; al devolverlo después, el
/// navegador haría caso a lo que él dijera y ejecutaría el script. Aquí el tipo que se
/// devuelve sale siempre de los bytes reales, así que lo peor que puede pasar es que una
/// imagen no se vea.</para>
/// <para>SVG queda FUERA de la lista a propósito, aunque sea una imagen: es un documento
/// XML que admite scripts, y no hay firma de bytes que distinga uno inofensivo de uno que
/// no lo sea.</para>
/// </summary>
public static class DetectorDeImagen
{
    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] FirmaJpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] FirmaRiff = [0x52, 0x49, 0x46, 0x46]; // "RIFF"
    private static readonly byte[] FirmaWebP = [0x57, 0x45, 0x42, 0x50]; // "WEBP"

    public static FormatoDeImagen Detectar(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(FirmaPng))
        {
            return FormatoDeImagen.Png;
        }

        if (bytes.StartsWith(FirmaJpeg))
        {
            return FormatoDeImagen.Jpeg;
        }

        // WebP es un contenedor RIFF: "RIFF" + 4 bytes de tamaño + "WEBP".
        if (bytes.Length >= 12 && bytes.StartsWith(FirmaRiff) && bytes.Slice(8, 4).SequenceEqual(FirmaWebP))
        {
            return FormatoDeImagen.WebP;
        }

        return FormatoDeImagen.Desconocido;
    }

    /// <summary>Tipo MIME correspondiente, o null si el formato no se reconoce.</summary>
    public static string? ContentType(FormatoDeImagen formato)
    {
        return formato switch
        {
            FormatoDeImagen.Jpeg => "image/jpeg",
            FormatoDeImagen.Png => "image/png",
            FormatoDeImagen.WebP => "image/webp",
            _ => null
        };
    }
}
