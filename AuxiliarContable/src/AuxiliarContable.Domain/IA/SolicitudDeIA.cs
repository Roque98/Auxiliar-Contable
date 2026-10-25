using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuxiliarContable.IA;

/// <summary>
/// Lo que se le pide al modelo. <see cref="Proposito"/> es una etiqueta corta y fija
/// («estado-de-cuenta», «conciliacion»): va en logs y métricas para saber en qué se gasta.
/// </summary>
public sealed class SolicitudDeIA
{
    public SolicitudDeIA(string proposito, string instrucciones, EsquemaDeSalida esquema, IReadOnlyList<ParteDeContenido> contenido)
    {
        if (string.IsNullOrWhiteSpace(proposito))
        {
            throw new ArgumentException("Falta el propósito.", nameof(proposito));
        }

        if (string.IsNullOrWhiteSpace(instrucciones))
        {
            throw new ArgumentException("Faltan las instrucciones.", nameof(instrucciones));
        }

        ArgumentNullException.ThrowIfNull(esquema);

        if (contenido is null || contenido.Count == 0)
        {
            throw new ArgumentException("No hay nada que leer.", nameof(contenido));
        }

        var bytes = contenido.Sum(c => c.BytesAproximados);
        if (bytes > IAConsts.MaxBytesPorPeticion)
        {
            throw new ArgumentException(
                $"El contenido pesa {bytes / (1024 * 1024)} MB codificado; el máximo es {IAConsts.MaxBytesPorPeticion / (1024 * 1024)} MB.",
                nameof(contenido));
        }

        Proposito = proposito;
        Instrucciones = instrucciones;
        Esquema = esquema;
        Contenido = contenido;
    }

    public string Proposito { get; }

    /// <summary>
    /// Va como mensaje de sistema y se marca para la caché de prompts: si pasa del mínimo del
    /// modelo (unos miles de tokens) y se repite, las siguientes llamadas la cobran a una décima parte;
    /// si es más corta, la marca no hace nada.
    /// </summary>
    public string Instrucciones { get; }

    public EsquemaDeSalida Esquema { get; }

    public IReadOnlyList<ParteDeContenido> Contenido { get; }

    /// <summary>Tope de tokens de la respuesta. Null = el de la configuración.</summary>
    public int? MaxTokens { get; init; }
}

/// <summary>
/// El JSON Schema que DEBE cumplir la respuesta. Se fuerza haciendo que el modelo «llame a
/// una herramienta» con ese esquema: es la forma de que conteste datos y no prosa.
/// </summary>
public sealed partial class EsquemaDeSalida
{
    public EsquemaDeSalida(string nombre, string descripcion, string jsonSchema)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > IAConsts.MaxLongitudNombreDeEsquema || !NombreValido().IsMatch(nombre))
        {
            throw new ArgumentException("El nombre del esquema solo admite letras, dígitos, «_» y «-» (hasta 64).", nameof(nombre));
        }

        if (string.IsNullOrWhiteSpace(descripcion))
        {
            throw new ArgumentException("Falta la descripción del esquema: el modelo la lee.", nameof(descripcion));
        }

        // Se comprueba aquí y no al llamar: un esquema mal escrito es un error de programación.
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(jsonSchema);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("El esquema no es JSON válido.", nameof(jsonSchema), ex);
        }

        using (documento)
        {
            if (documento.RootElement.ValueKind != JsonValueKind.Object
                || !documento.RootElement.TryGetProperty("type", out var tipo)
                || tipo.GetString() != "object")
            {
                throw new ArgumentException("El esquema tiene que ser un objeto JSON Schema con \"type\": \"object\".", nameof(jsonSchema));
            }
        }

        Nombre = nombre;
        Descripcion = descripcion;
        JsonSchema = jsonSchema;
    }

    public string Nombre { get; }

    public string Descripcion { get; }

    public string JsonSchema { get; }

    [GeneratedRegex("^[a-zA-Z0-9_-]+$")]
    private static partial Regex NombreValido();
}

/// <summary>Un trozo de lo que se manda: texto, una imagen o un PDF.</summary>
public abstract class ParteDeContenido
{
    /// <summary>Lo que ocupará en la petición (base64 engorda 4/3).</summary>
    internal abstract long BytesAproximados { get; }

    public static ParteDeContenido Texto(string texto) => new TextoDeContenido(texto);

    public static ParteDeContenido Imagen(byte[] datos, string tipoMime) => new ImagenDeContenido(datos, tipoMime);

    public static ParteDeContenido Pdf(byte[] datos) => new PdfDeContenido(datos);

    /// <summary>Imagen o PDF según el tipo MIME; cualquier otro tipo lanza.</summary>
    public static ParteDeContenido Archivo(byte[] datos, string tipoMime) =>
        string.Equals(tipoMime, IAConsts.TipoPdf, StringComparison.OrdinalIgnoreCase) ? Pdf(datos) : Imagen(datos, tipoMime);

    internal static long Base64(int bytes) => (bytes + 2L) / 3 * 4;
}

public sealed class TextoDeContenido : ParteDeContenido
{
    public TextoDeContenido(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new ArgumentException("Texto vacío.", nameof(texto));
        }

        Valor = texto;
    }

    public string Valor { get; }

    internal override long BytesAproximados => System.Text.Encoding.UTF8.GetByteCount(Valor);
}

public sealed class ImagenDeContenido : ParteDeContenido
{
    public ImagenDeContenido(byte[] datos, string tipoMime)
    {
        if (datos is null || datos.Length == 0)
        {
            throw new ArgumentException("Imagen vacía.", nameof(datos));
        }

        var tipo = tipoMime?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IAConsts.TiposDeImagen.Contains(tipo))
        {
            throw new ArgumentException(
                $"El modelo no lee imágenes «{tipoMime}». Admite: {string.Join(", ", IAConsts.TiposDeImagen)}.",
                nameof(tipoMime));
        }

        if (datos.Length > IAConsts.MaxBytesPorImagen)
        {
            throw new ArgumentException(
                $"La imagen pesa {datos.Length / 1024} KB; el máximo es {IAConsts.MaxBytesPorImagen / 1024} KB.",
                nameof(datos));
        }

        Datos = datos;
        TipoMime = tipo;
    }

    public byte[] Datos { get; }

    public string TipoMime { get; }

    internal override long BytesAproximados => Base64(Datos.Length);
}

public sealed class PdfDeContenido : ParteDeContenido
{
    public PdfDeContenido(byte[] datos)
    {
        if (datos is null || datos.Length == 0)
        {
            throw new ArgumentException("PDF vacío.", nameof(datos));
        }

        Datos = datos;
    }

    public byte[] Datos { get; }

    internal override long BytesAproximados => Base64(Datos.Length);
}
