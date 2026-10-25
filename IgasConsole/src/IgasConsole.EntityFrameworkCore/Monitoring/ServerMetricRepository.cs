using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.Data;
using IgasConsole.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace IgasConsole.Monitoring;

public class ServerMetricRepository :
    EfCoreRepository<IgasConsoleDbContext, ServerMetric, Guid>,
    IServerMetricRepository
{
    public ServerMetricRepository(IDbContextProvider<IgasConsoleDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<List<MetricSeriesPoint>> GetSeriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int bucketMinutes,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        var sql = SqlResource.Load("Monitoring/Sql/GetMetricSeries.sql");

        return await dbContext.Database
            .SqlQueryRaw<MetricSeriesPoint>(sql, fromUtc, toUtc, bucketMinutes)
            .ToListAsync(cancellationToken);
    }

    public async Task<DateTime?> GetLastSampleTimeAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // MAX sobre la columna indexada: PostgreSQL lo resuelve con un salto al final del
        // índice, sin leer filas.
        return await dbContext.Set<ServerMetric>()
            .MaxAsync(m => (DateTime?)m.Timestamp, cancellationToken);
    }

    public async Task<ServerMetric?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // Mismo índice que el MAX de arriba, recorrido al revés y una sola fila.
        return await dbContext.Set<ServerMetric>()
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DatabaseStats?> GetDatabaseStatsAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        var sql = SqlResource.Load("Monitoring/Sql/GetDatabaseStats.sql");

        var filas = await dbContext.Database
            .SqlQueryRaw<DatabaseStats>(sql)
            .ToListAsync(cancellationToken);

        return filas.FirstOrDefault();
    }

    public async Task<int> PurgeOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();

        // ExecuteDeleteAsync borra en la base de un golpe, sin traer las entidades a
        // memoria: son decenas de miles de filas y no hay lógica de dominio que aplicar.
        return await dbContext.Set<ServerMetric>()
            .Where(m => m.Timestamp < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
