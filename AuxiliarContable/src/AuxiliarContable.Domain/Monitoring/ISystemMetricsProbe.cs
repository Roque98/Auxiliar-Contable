namespace AuxiliarContable.Monitoring;

/// <summary>
/// Lee CPU, memoria y disco de la máquina. La implementación es específica del sistema
/// operativo, por eso el dominio solo conoce la interfaz.
/// </summary>
public interface ISystemMetricsProbe
{
    SystemSample Read();
}
