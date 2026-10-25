using System;
using System.Collections.Generic;
using IgasConsole.Monitoring;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Acumula en memoria los rechazos de rate limiting hasta que el recolector los drena.
/// A diferencia de AppMetricsAccumulator, aquí un candado (lock) alcanza: Record solo se
/// llama desde OnRejected, un camino que ya es el lento (una petición rechazada), no en
/// cada petición como el acumulador de peticiones.
/// </summary>
public class RateLimitAccumulator : IRateLimitAccumulator, ISingletonDependency
{
    private readonly object _candado = new();
    private Dictionary<(string Ip, string Endpoint), RateLimitRejectionSample> _muestras = new();

    public void Record(string ipAddress, string endpoint)
    {
        var clave = (ipAddress, endpoint);
        var ahora = DateTime.UtcNow;

        lock (_candado)
        {
            if (_muestras.TryGetValue(clave, out var existente))
            {
                existente.Count++;
                existente.LastOccurredUtc = ahora;
            }
            else
            {
                _muestras[clave] = new RateLimitRejectionSample
                {
                    IpAddress = ipAddress,
                    Endpoint = endpoint,
                    Count = 1,
                    FirstOccurredUtc = ahora,
                    LastOccurredUtc = ahora
                };
            }
        }
    }

    public List<RateLimitRejectionSample> DrainAndReset()
    {
        lock (_candado)
        {
            var muestras = _muestras;
            _muestras = new Dictionary<(string Ip, string Endpoint), RateLimitRejectionSample>();
            return new List<RateLimitRejectionSample>(muestras.Values);
        }
    }
}
