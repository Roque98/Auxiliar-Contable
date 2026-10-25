namespace AuxiliarContable.EstadosDeCuenta;

public static class EstadoDeCuentaConsts
{
    public const int MaxBancoLength = 100;
    public const int MaxCuentaLength = 20;
    public const int MaxConceptoLength = 500;
    public const int MaxReferenciaLength = 100;

    /// <summary>Diferencia que se tolera al cuadrar (redondeos de centavos).</summary>
    public const decimal Tolerancia = 0.01m;

    /// <summary>Un estado de cuenta mensual de un negocio pequeño rara vez pasa de unos cientos.</summary>
    public const int MaxMovimientos = 2000;
}

/// <summary>En qué punto de la revisión está un estado de cuenta.</summary>
public enum RevisionDelEstadoDeCuenta
{
    /// <summary>Leído y CUADRA: saldo inicial + abonos − cargos = saldo final. Falta que una persona lo dé por bueno.</summary>
    PorConfirmar = 0,

    /// <summary>Leído pero NO cuadra (o le faltan saldos): necesita revisión humana antes de usarse.</summary>
    RequiereRevision = 1,

    /// <summary>Una persona lo revisó y lo dio por bueno.</summary>
    Confirmado = 2,
}
