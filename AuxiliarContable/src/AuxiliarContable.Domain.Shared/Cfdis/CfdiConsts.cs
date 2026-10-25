namespace AuxiliarContable.Cfdis;

public static class CfdiConsts
{
    public const int MaxRfcLength = 13;
    public const int MaxNombreLength = 300;
    public const int UuidLength = 36;
    public const int MaxConceptoLength = 1000;

    /// <summary>Archivos por subida desde la bóveda (un mes de facturas cabe holgado).</summary>
    public const int MaxArchivosPorSubida = 50;
}

/// <summary>Desde el punto de vista del cliente: ¿lo emitió él (venta) o se lo emitieron (gasto)?</summary>
public enum DireccionDelCfdi
{
    /// <summary>Ni emisor ni receptor coinciden con el RFC del perfil (o aún no hay RFC).</summary>
    Desconocida = 0,
    Emitido = 1,
    Recibido = 2,
}
