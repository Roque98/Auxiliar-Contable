using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace IgasConsole.Monitoring;

/// <summary>
/// Un rollup por minuto de peticiones rechazadas por rate limiting desde una misma IP a
/// un mismo endpoint. Igual que <c>ServerMetric</c>, NO es multi-tenant ni tiene soft
/// delete: es una señal de infraestructura, no un dato de negocio, y la purga por
/// retención tiene que borrar de verdad.
/// </summary>
public class RateLimitEvent : Entity<Guid>
{
    /// <summary>Minuto del ciclo del recolector en el que se registró (mismo Timestamp
    /// que la muestra de ServerMetric de ese ciclo). SIEMPRE UTC.</summary>
    public DateTime WindowStartUtc { get; protected set; }

    public string IpAddress { get; protected set; }

    /// <summary>Patrón de ruta ("/api/app/product/{id}"), no la URL con el id real.</summary>
    public string Endpoint { get; protected set; }

    public int RejectionCount { get; protected set; }

    public DateTime FirstRejectedUtc { get; protected set; }

    public DateTime LastRejectedUtc { get; protected set; }

    protected RateLimitEvent()
    {
        IpAddress = string.Empty;
        Endpoint = string.Empty;
    }

    public RateLimitEvent(
        Guid id,
        DateTime windowStartUtc,
        string ipAddress,
        string endpoint,
        int rejectionCount,
        DateTime firstRejectedUtc,
        DateTime lastRejectedUtc)
        : base(id)
    {
        ExigirUtc(windowStartUtc, nameof(windowStartUtc));
        ExigirUtc(firstRejectedUtc, nameof(firstRejectedUtc));
        ExigirUtc(lastRejectedUtc, nameof(lastRejectedUtc));

        WindowStartUtc = windowStartUtc;
        IpAddress = Check.NotNullOrWhiteSpace(
            ipAddress, nameof(ipAddress), MonitoringConsts.MaxIpAddressLength);
        Endpoint = Check.NotNullOrWhiteSpace(
            endpoint, nameof(endpoint), MonitoringConsts.MaxEndpointLength);
        RejectionCount = rejectionCount;
        FirstRejectedUtc = firstRejectedUtc;
        LastRejectedUtc = lastRejectedUtc;
    }

    private static void ExigirUtc(DateTime valor, string nombre)
    {
        if (valor.Kind != DateTimeKind.Utc)
        {
            throw new AbpException(
                $"RateLimitEvent.{nombre} debe ser UTC: la columna es timestamptz y Npgsql " +
                "lanza en tiempo de ejecución si el Kind no es Utc.");
        }
    }
}
