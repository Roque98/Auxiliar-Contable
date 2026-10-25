namespace AuxiliarContable.Documentos;

public static class DocumentoConsts
{
    public const string ContainerName = "documentos";

    /// <summary>
    /// Tope por archivo. Un estado de cuenta de un año en PDF ronda 2-5 MB; 20 MB deja holgura
    /// para escaneos sin dejar que alguien llene el disco de una vez.
    /// </summary>
    public const int MaxBytes = 20 * 1024 * 1024;

    public const int MaxNombreLength = 255;

    public const int MaxTipoMimeLength = 100;

    /// <summary>SHA-256 en hexadecimal.</summary>
    public const int HashLength = 64;
}

/// <summary>Qué es el documento. Lo decide quien sube (o, más adelante, la clasificación).</summary>
public enum TipoDeDocumento
{
    Otro = 0,
    ConstanciaDeSituacionFiscal = 1,
    Cfdi = 2,
    EstadoDeCuenta = 3,
}

/// <summary>En qué punto del proceso está.</summary>
public enum EstadoDeDocumento
{
    /// <summary>Guardado, todavía sin leer.</summary>
    Recibido = 0,

    /// <summary>Leído: sus datos ya se extrajeron (y esperan confirmación si hace falta).</summary>
    Procesado = 1,

    /// <summary>No se pudo leer (ilegible, formato inesperado). Queda para revisión humana.</summary>
    ConError = 2,
}
