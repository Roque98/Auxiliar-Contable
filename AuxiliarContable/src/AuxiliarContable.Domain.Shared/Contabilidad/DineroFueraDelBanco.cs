namespace AuxiliarContable.Contabilidad;

/// <summary>Dinero que no se ve en el estado de cuenta: efectivo, y lo comprado o vendido sin pagar todavía.</summary>
public enum TipoDeMovimientoFueraDelBanco
{
    /// <summary>Venta o entrada en efectivo; el corte de caja del día es una.</summary>
    EntradaEnEfectivo = 0,

    /// <summary>Gasto o salida en efectivo.</summary>
    SalidaEnEfectivo = 1,

    /// <summary>Compra que no se ha pagado (a crédito, por pagar).</summary>
    CompraACredito = 2,

    /// <summary>Venta que no se ha cobrado.</summary>
    VentaACredito = 3,
}

public static class DineroFueraDelBancoConsts
{
    public const int MaxConceptoLength = 256;
    public const int MaxContraparteLength = 128;
    public const int MaxNotaLength = 512;
}
