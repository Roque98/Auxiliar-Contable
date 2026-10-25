using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace IgasConsole.Monitoring;

public interface IRateLimitEventRepository : IRepository<RateLimitEvent, Guid>
{
    /// <summary>Eventos desde una fecha, más recientes primero. Es un listado simple para
    /// el panel, no una agregación: no hace falta SqlQueryRaw.</summary>
    Task<List<RateLimitEvent>> GetRecentAsync(
        DateTime sinceUtc, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>Borra los eventos anteriores a la fecha dada. Devuelve cuántos borró.</summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
