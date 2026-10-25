using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace AuxiliarContable.Jobs;

public class JobMaintenanceRepository : IJobMaintenanceRepository, ITransientDependency
{
    private readonly IDbContextProvider<AuxiliarContableDbContext> _dbContextProvider;

    public JobMaintenanceRepository(IDbContextProvider<AuxiliarContableDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<List<JobEntry>> GetRecentAsync(
        JobState? state,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var consulta = Filtrar(dbContext.Set<BackgroundJobRecord>(), state);

        // Por creación descendente y NO por NextTryTime: lo último que se encoló es lo que
        // se viene a mirar cuando algo acaba de fallar. Ordenar por el próximo intento
        // pondría arriba justamente lo más viejo y ya casi abandonado.
        return await consulta
            .OrderByDescending(j => j.CreationTime)
            .Take(maxResults)
            .Select(j => new JobEntry
            {
                Id = j.Id,
                JobName = j.JobName,
                JobArgs = j.JobArgs,
                TryCount = j.TryCount,
                CreationTime = j.CreationTime,
                NextTryTime = j.NextTryTime,
                LastTryTime = j.LastTryTime,
                IsAbandoned = j.IsAbandoned,
                CompletionTime = j.CompletionTime
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<JobState, int>> GetCountsByStateAsync(
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Se agrupa por las tres columnas crudas en vez de por el estado derivado: EF no
        // puede traducir el método que lo calcula, y traerse la tabla entera para contarla
        // en memoria sería justo lo que este resumen intenta evitar. Son como mucho cuatro
        // combinaciones, así que derivar después no cuesta nada.
        var crudos = await dbContext.Set<BackgroundJobRecord>()
            .GroupBy(j => new
            {
                j.IsAbandoned,
                Completado = j.CompletionTime != null,
                Reintentado = j.TryCount > 0
            })
            .Select(g => new
            {
                g.Key.IsAbandoned,
                g.Key.Completado,
                g.Key.Reintentado,
                Cuantos = g.Count()
            })
            .ToListAsync(cancellationToken);

        var resultado = new Dictionary<JobState, int>();

        foreach (var fila in crudos)
        {
            var estado = JobEntry.Derivar(
                fila.IsAbandoned,
                // Solo importa si tiene valor, no cuál: Derivar únicamente comprueba el null.
                fila.Completado ? DateTime.UnixEpoch : null,
                fila.Reintentado ? 1 : 0);

            resultado[estado] = resultado.GetValueOrDefault(estado) + fila.Cuantos;
        }

        return resultado;
    }

    /// <summary>
    /// Traduce el estado a las columnas reales.
    /// <para>Tiene que quedar en SQL: si se filtrara en memoria, el <c>Take</c> recortaría
    /// antes de filtrar y pedir «abandonados» devolvería cero con la cola llena de ellos.
    /// Por eso se repite aquí la regla de <see cref="JobEntry.Derivar"/> en vez de
    /// llamarla —EF no sabe traducir un método— y por eso hay un test que compara las dos
    /// formas de calcular el estado.</para>
    /// </summary>
    private static IQueryable<BackgroundJobRecord> Filtrar(
        IQueryable<BackgroundJobRecord> consulta, JobState? state)
    {
        return state switch
        {
            JobState.Completado => consulta.Where(j => j.CompletionTime != null),
            JobState.Abandonado => consulta.Where(j => j.CompletionTime == null && j.IsAbandoned),
            JobState.Reintentando => consulta.Where(j =>
                j.CompletionTime == null && !j.IsAbandoned && j.TryCount > 0),
            JobState.Esperando => consulta.Where(j =>
                j.CompletionTime == null && !j.IsAbandoned && j.TryCount == 0),
            _ => consulta
        };
    }
}
