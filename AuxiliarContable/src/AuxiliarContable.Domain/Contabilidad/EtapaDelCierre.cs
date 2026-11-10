namespace AuxiliarContable.Contabilidad;

/// <summary>
/// En qué va el cierre de un mes. Una sola regla para la pantalla del cierre y para la cartera, para
/// que las dos digan lo mismo.
/// </summary>
public static class EtapaDelCierre
{
    /// <param name="huellaActual">De los estados de cuenta y facturas que hay hoy en el mes.</param>
    /// <param name="huellaConciliada">De los que entraron a la última conciliación; null si no hay.</param>
    public static EtapaDelPeriodo Calcular(bool cerrado, int estadosDeCuenta, string huellaActual, string? huellaConciliada, int partidasPendientes)
    {
        if (cerrado)
        {
            return EtapaDelPeriodo.Cerrado;
        }

        if (estadosDeCuenta == 0)
        {
            return EtapaDelPeriodo.FaltanDocumentos;
        }

        if (huellaConciliada != huellaActual)
        {
            return EtapaDelPeriodo.PorConciliar;
        }

        return partidasPendientes > 0 ? EtapaDelPeriodo.ConPendientes : EtapaDelPeriodo.ListoParaCerrar;
    }
}
