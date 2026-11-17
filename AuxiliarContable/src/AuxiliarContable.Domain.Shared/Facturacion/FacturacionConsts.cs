namespace AuxiliarContable.Facturacion;

public enum EstadoDeSolicitudDeFactura
{
    /// <summary>La pidió el cliente; el despacho no la ha atendido.</summary>
    Pendiente = 0,

    /// <summary>El despacho la timbró y dejó el XML (y el PDF) en la bóveda.</summary>
    Emitida = 1,

    /// <summary>El despacho no la pudo emitir y dijo por qué.</summary>
    Rechazada = 2,

    /// <summary>El cliente la retiró antes de que se atendiera.</summary>
    Cancelada = 3,
}

public static class FacturacionConsts
{
    public const int RfcLength = 13;
    public const int MaxNombreLength = 254;
    public const int CodigoPostalLength = 5;
    public const int ClaveCortaLength = 4;
    public const int MaxConceptos = 50;
    public const int MaxDescripcionLength = 1000;
    public const int MaxNotaLength = 1000;

    /// <summary>Tasas de IVA que se aceptan en un concepto (null = exento).</summary>
    public static readonly decimal[] TasasDeIva = [0.16m, 0.08m, 0m];
}
