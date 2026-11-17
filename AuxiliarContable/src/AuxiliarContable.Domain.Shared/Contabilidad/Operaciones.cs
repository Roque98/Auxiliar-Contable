namespace AuxiliarContable.Contabilidad;

/// <summary>Las tres patas de una operación: su factura, la transacción (el dinero) y su control interno.</summary>
public enum PataDeOperacion
{
    Factura = 0,
    Transaccion = 1,
    ControlInterno = 2,
}

/// <summary>En qué quedó una pata.</summary>
public enum EstadoDePata
{
    /// <summary>Nadie ha dicho nada todavía (gris).</summary>
    Pendiente = 0,

    /// <summary>Amarrada: UUID, referencia o folio, documento (verde).</summary>
    Ligada = 1,

    /// <summary>Se declaró que no hay: «no se facturó», «no tengo la referencia», «no tengo comprobante» (ámbar).</summary>
    NoHay = 2,
}

/// <summary>Cómo se movió el dinero de una operación.</summary>
public enum FormaDeTransaccion
{
    /// <summary>Transferencia, tarjeta o depósito: se amarra con su referencia o folio (la del comprobante o la del estado de cuenta).</summary>
    Banco = 0,

    Efectivo = 1,

    /// <summary>Aún no se paga (o cobra): queda por pagar en «Efectivo y crédito».</summary>
    Credito = 2,
}

/// <summary>Hacia dónde va el dinero.</summary>
public enum SentidoDeOperacion
{
    /// <summary>Compra, gasto o pago (factura recibida).</summary>
    Salida = 0,

    /// <summary>Venta o cobro (factura emitida).</summary>
    Entrada = 1,
}

public static class OperacionConsts
{
    public const int MaxDescripcionLength = 256;
    public const int MaxContraparteLength = 256;
    public const int MaxReferenciaLength = 100;
    public const int MaxMotivoLength = 500;
    public const int UuidLength = 36;
}
