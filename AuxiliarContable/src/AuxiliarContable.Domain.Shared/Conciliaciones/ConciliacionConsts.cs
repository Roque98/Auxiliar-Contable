namespace AuxiliarContable.Conciliaciones;

public static class ConciliacionConsts
{
    /// <summary>Días de diferencia que se aceptan entre la factura y el movimiento del banco.</summary>
    public const int DiasDeTolerancia = 5;

    /// <summary>Diferencia de monto aceptada (centavos de redondeo).</summary>
    public const decimal ToleranciaDeMonto = 0.01m;

    public const int MaxMotivoLength = 500;
}

public enum EstadoDeConciliacion
{
    /// <summary>La propuso el sistema por reglas. Falta que una persona la acepte.</summary>
    Propuesta = 0,

    /// <summary>Una persona la aceptó (o la hizo a mano).</summary>
    Confirmada = 1,

    /// <summary>Una persona dijo que no: no se vuelve a proponer esa pareja.</summary>
    Rechazada = 2,
}
