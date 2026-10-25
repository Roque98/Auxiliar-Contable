using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace AuxiliarContable.Jobs;

public class JobFailureRepository : IJobFailureRepository, ITransientDependency
{
    private readonly IDbContextProvider<AuxiliarContableDbContext> _dbContextProvider;

    public JobFailureRepository(IDbContextProvider<AuxiliarContableDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task InsertAsync(JobFailure failure, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.Set<JobFailure>().AddAsync(failure, cancellationToken);
    }

    public async Task<Dictionary<Guid, JobFailure>> GetLatestByJobAsync(
        IReadOnlyCollection<Guid> jobIds,
        CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
        {
            return new Dictionary<Guid, JobFailure>();
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Se traen los fallos de esos trabajos y se queda el más reciente de cada uno ya en
        // memoria. Podría hacerse con una ventana en SQL, pero son los fallos de como mucho
        // una página de trabajos: la lista está acotada por el tope del panel, no por el
        // tamaño de la tabla.
        var filas = await dbContext.Set<JobFailure>()
            .Where(f => jobIds.Contains(f.JobId))
            .OrderByDescending(f => f.AttemptedUtc)
            .ToListAsync(cancellationToken);

        return filas
            .GroupBy(f => f.JobId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public async Task<List<JobFailure>> GetHistoryAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        return await dbContext.Set<JobFailure>()
            .Where(f => f.JobId == jobId)
            .OrderByDescending(f => f.AttemptedUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> PurgeOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        return await dbContext.Set<JobFailure>()
            .Where(f => f.AttemptedUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
