namespace AuxiliarContable.IA;

/// <summary>
/// Límites de lo que se le puede mandar al modelo de IA. Son los de la API de Anthropic
/// (docs.anthropic.com, «Vision» y «PDF support»), comprobados ANTES de llamar: un envío que
/// la API va a rechazar se cobra igual en tiempo y no explica nada al usuario.
/// </summary>
public static class IAConsts
{
    /// <summary>Una imagen no puede pasar de 5 MB.</summary>
    public const int MaxBytesPorImagen = 5 * 1024 * 1024;

    /// <summary>
    /// La petición entera (con todo en base64, que engorda un tercio) no puede pasar de 32 MB.
    /// Un PDF de 24 MB ya no cabe.
    /// </summary>
    public const int MaxBytesPorPeticion = 32 * 1024 * 1024;

    /// <summary>Formatos de imagen que entiende el modelo. HEIC (fotos de iPhone) NO: hay que convertirla antes.</summary>
    public static readonly string[] TiposDeImagen = ["image/jpeg", "image/png", "image/gif", "image/webp"];

    public const string TipoPdf = "application/pdf";

    /// <summary>Nombre de la herramienta con la que se fuerza la salida estructurada: letras, dígitos, «_» y «-», hasta 64.</summary>
    public const int MaxLongitudNombreDeEsquema = 64;
}
