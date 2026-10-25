using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace IgasConsole.Monitoring;

public interface IServerMetricRepository : IRepository<ServerMetric, Guid>
{
    /// <summary>
    /// Serie agregada entre dos instantes, agrupada en cubos de N minutos. La agregación
    /// la hace la base (no C#): traer 43.000 filas para pintar 60 puntos sería absurdo.
    /// </summary>
    Task<List<MetricSeriesPoint>> GetSeriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int bucketMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>Marca de la última muestra registrada, para detectar que el recolector
    /// dejó de recolectar. Null si no hay ninguna.</summary>
    Task<DateTime?> GetLastSampleTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// La última muestra completa. La usa el panel de estado para dar salud y tiempo en
    /// marcha sin volver a sondear el sistema: el recolector ya lo hizo hace un minuto.
    /// </summary>
    Task<ServerMetric?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>Tamaño de la base y conexiones abiertas. Lo sabe la base, no el proceso.</summary>
    Task<DatabaseStats?> GetDatabaseStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>Borra las muestras anteriores a la fecha dada. Devuelve cuántas borró.</summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
