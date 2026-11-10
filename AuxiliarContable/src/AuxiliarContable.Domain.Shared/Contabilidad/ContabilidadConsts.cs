namespace AuxiliarContable.Contabilidad;

// El cierre mensual, como en un sistema contable (CONTPAQi, Aspel COI, Contalink): cada mes de cada
// cliente es un PERIODO que se abre, se concilia, se depuran sus partidas y se cierra. Un periodo
// cerrado ya no acepta cambios en sus documentos hasta que alguien del despacho lo reabre.

public static class ContabilidadConsts
{
    /// <summary>Huella de un movimiento (ver <c>ClaveDeMovimiento</c>): SHA-256 truncado, en hexadecimal.</summary>
    public const int ClaveLength = 32;

    /// <summary>Huella de los archivos que entraron a una conciliación: SHA-256 en hexadecimal.</summary>
    public const int HuellaLength = 64;

    public const int MaxNotaLength = 500;

    public const int MaxRespuestaLength = 1000;

    public const int MaxDescripcionLength = 300;
}

/// <summary>Dónde va un mes en el cierre. El orden es el del trabajo: se avanza de arriba abajo.</summary>
public enum EtapaDelPeriodo
{
    /// <summary>Falta el estado de cuenta o no hay facturas del mes.</summary>
    FaltanDocumentos = 0,

    /// <summary>Están los documentos y no hay conciliación (o la que hay ya no corresponde a los archivos).</summary>
    PorConciliar = 1,

    /// <summary>Conciliado, pero quedan movimientos sin factura por resolver.</summary>
    ConPendientes = 2,

    /// <summary>Todo resuelto: el contador puede cerrarlo.</summary>
    ListoParaCerrar = 3,

    Cerrado = 4,
}

/// <summary>Qué decidió el despacho de un movimiento del banco que no tiene factura.</summary>
public enum ResolucionDePartida
{
    SinResolver = 0,

    /// <summary>Se le pregunta al cliente qué es; él responde desde su pantalla.</summary>
    PreguntarAlCliente = 1,

    /// <summary>Dinero que va de una cuenta del cliente a otra suya: no lleva factura.</summary>
    TraspasoPropio = 2,

    /// <summary>Es un gasto o ingreso real sin CFDI (no deducible, préstamo, aportación…).</summary>
    SinFactura = 3,

    /// <summary>La factura existe pero aún no llega a la bóveda: se espera.</summary>
    FacturaPorLlegar = 4,

    /// <summary>Ya se revisó y no requiere nada más (comisiones, intereses, impuestos retenidos…).</summary>
    Revisado = 5,
}
