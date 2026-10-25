using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace IgasConsole.Monitoring;

public class RateLimitEventRepository :
    EfCoreRepository<IgasConsoleDbContext, RateLimitEvent, Guid>,
    IRateLimitEventRepository
{
    public RateLimitEventRepository(IDbContextProvider<IgasConsoleDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<List<RateLimitEvent>> GetRecentAsync(
        DateTime sinceUtc, int maxResults, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        return await dbContext.Set<RateLimitEvent>()
            .Where(e => e.LastRejectedUtc >= sinceUtc)
            .OrderByDescending(e => e.LastRejectedUtc)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> PurgeOlderThanAsync(
        DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        return await dbContext.Set<RateLimitEvent>()
            .Where(e => e.WindowStartUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
