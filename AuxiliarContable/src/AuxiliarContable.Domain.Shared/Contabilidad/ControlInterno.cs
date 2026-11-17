namespace AuxiliarContable.Contabilidad;

/// <summary>De dónde es la operación a la que se le pone evidencia.</summary>
public enum OrigenDeOperacion
{
    /// <summary>Un movimiento del estado de cuenta (se reconoce por su clave, ver ClaveDeMovimiento).</summary>
    Banco = 0,

    /// <summary>Un registro de «Efectivo y crédito».</summary>
    FueraDelBanco = 1,
}

/// <summary>Qué demuestra la evidencia.</summary>
public enum TipoDeEvidencia
{
    /// <summary>Transferencia, voucher, recibo: que el dinero salió o entró.</summary>
    ComprobanteDePago = 0,

    /// <summary>Contrato, orden de servicio.</summary>
    Contrato = 1,

    /// <summary>Lo que se recibió: entregable, reporte, foto de la mercancía o del trabajo.</summary>
    EntregableOFoto = 2,

    /// <summary>Pedido, cotización, orden de compra.</summary>
    PedidoOCotizacion = 3,

    /// <summary>Lo que explica el cliente con sus palabras.</summary>
    Testimonio = 4,

    Otro = 5,
}

/// <summary>Cómo va la documentación de una operación.</summary>
public enum EstadoDeEvidencia
{
    /// <summary>Entradas de dinero y traspasos entre cuentas propias: aquí no se pide nada.</summary>
    NoAplica = 0,

    /// <summary>Tiene lo que necesita.</summary>
    Completa = 1,

    /// <summary>Salida con factura, pero sin nada que pruebe que la operación ocurrió (materialidad).</summary>
    FaltaMaterialidad = 2,

    /// <summary>Salida sin factura y sin comprobante ni testimonio de qué fue.</summary>
    FaltaComprobante = 3,
}

public static class ControlInternoConsts
{
    public const int MaxNotaLength = 2000;
    public const int MaxDescripcionLength = 512;
}
