namespace AuxiliarContable.Monitoring;

/// <summary>
/// Acumula en memoria lo que pasa en cada petición y lo entrega una vez por intervalo.
/// Contar en memoria y escribir una fila por minuto, en vez de una por petición, es la
/// diferencia entre monitorizar y duplicar la carga de la base.
/// </summary>
public interface IAppMetricsAccumulator
{
    void Record(int statusCode, double elapsedMs);

    /// <summary>Devuelve lo acumulado y pone el contador a cero, de forma atómica.</summary>
    RequestSample DrainAndReset();
}
