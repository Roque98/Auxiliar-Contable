using System;
using System.Collections.Generic;

namespace AuxiliarContable.Monitoring;

/// <summary>
/// Acumula en memoria las peticiones rechazadas por rate limiting hasta que el
/// recolector las drena, una vez por minuto. Mismo contrato que
/// <c>IAppMetricsAccumulator</c>, pero solo se llama en el camino ya lento de un rechazo
/// (nunca en cada petición), así que no necesita ser lock-free.
/// </summary>
public interface IRateLimitAccumulator
{
    void Record(string ipAddress, string endpoint);

    /// <summary>Devuelve lo acumulado, agrupado por IP+endpoint, y lo pone a cero.</summary>
    List<RateLimitRejectionSample> DrainAndReset();
}

/// <summary>Rechazos acumulados de una IP a un endpoint durante el intervalo que se
/// acaba de cerrar.</summary>
public class RateLimitRejectionSample
{
    public string IpAddress { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime FirstOccurredUtc { get; set; }
    public DateTime LastOccurredUtc { get; set; }
}
