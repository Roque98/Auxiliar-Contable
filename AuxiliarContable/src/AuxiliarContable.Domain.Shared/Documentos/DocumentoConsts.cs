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

    /// <summary>Folio fiscal (UUID) de un CFDI: 36 caracteres.</summary>
    public const int UuidLength = 36;

    /// <summary>«Vigente», «Cancelado»… tal como lo dice el portal del SAT.</summary>
    public const int MaxEstatusSatLength = 40;
}

/// <summary>Qué es el documento. Lo decide quien sube (o, más adelante, la clasificación).</summary>
public enum TipoDeDocumento
{
    Otro = 0,
    ConstanciaDeSituacionFiscal = 1,
    Cfdi = 2,
    EstadoDeCuenta = 3,
    Ticket = 4,

    /// <summary>Comprobante de una transacción suelta: transferencia, SPEI, voucher, recibo de pago.</summary>
    ComprobanteDePago = 5,

    /// <summary>Control interno: nota de venta o de compra, vale, comprobante interno de la operación.</summary>
    ControlInterno = 6,

    /// <summary>Información de nómina que trae el auxiliar: lista de raya, incidencias, SUA, cédulas del IMSS.</summary>
    Nomina = 7,

    /// <summary>
    /// Catálogo de cuentas propio del cliente (XML del Anexo 24 de la RMF). El más reciente es el que se usa para presentar
    /// la balanza, el auxiliar y la contabilidad electrónica con sus números de cuenta.
    /// </summary>
    CatalogoDeCuentas = 8,

    /// <summary>Balanza de comprobación con la que llega el cliente (XML del Anexo 24): respaldo de sus saldos de apertura.</summary>
    BalanzaDeComprobacion = 9,
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
