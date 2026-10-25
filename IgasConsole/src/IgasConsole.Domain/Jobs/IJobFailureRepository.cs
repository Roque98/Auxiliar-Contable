using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IgasConsole.Jobs;

/// <summary>Historial de intentos fallidos. Ver <see cref="JobFailure"/>.</summary>
public interface IJobFailureRepository
{
    Task InsertAsync(JobFailure failure, CancellationToken cancellationToken = default);

    /// <summary>
    /// El último fallo de cada uno de los trabajos dados, en una sola consulta.
    /// <para>De una en una serían N+1 consultas para pintar una tabla, y esta pantalla se
    /// abre justo cuando algo va mal —el peor momento para cargar la base de datos.</para>
    /// </summary>
    Task<Dictionary<Guid, JobFailure>> GetLatestByJobAsync(
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default);

    /// <summary>Todos los intentos de un trabajo, del más reciente al más antiguo.</summary>
    Task<List<JobFailure>> GetHistoryAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<int> PurgeOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default);
}
