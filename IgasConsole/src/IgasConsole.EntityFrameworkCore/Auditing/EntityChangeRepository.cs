using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace IgasConsole.Auditing;

public class EntityChangeRepository : IEntityChangeRepository, ITransientDependency
{
    private readonly IDbContextProvider<IgasConsoleDbContext> _dbContextProvider;

    public EntityChangeRepository(IDbContextProvider<IgasConsoleDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<List<EntityChangeEntry>> GetRecentAsync(
        DateTime sinceUtc,
        int maxResults,
        string? entityTypeFullName = null,
        string? entityId = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // El usuario NO está en EntityChange: vive en el AuditLog padre. Se cruza aquí, en
        // la consulta, en vez de traer los cambios y luego buscar sus registros uno a uno
        // —que serían N+1 consultas para pintar una tabla—.
        var consulta =
            from cambio in Filtrar(dbContext, sinceUtc, entityTypeFullName, entityId)
            join log in dbContext.Set<AuditLog>() on cambio.AuditLogId equals log.Id
            select new { cambio, log };

        if (!string.IsNullOrWhiteSpace(userName))
        {
            var buscado = userName.Trim();
            consulta = consulta.Where(x =>
                x.log.UserName != null && EF.Functions.ILike(x.log.UserName, "%" + buscado + "%"));
        }

        var filas = await consulta
            .OrderByDescending(x => x.cambio.ChangeTime)
            .Take(maxResults)
            .Select(x => new
            {
                x.cambio.Id,
                x.cambio.ChangeTime,
                x.cambio.ChangeType,
                x.cambio.EntityTypeFullName,
                x.cambio.EntityId,
                x.log.UserName,
                x.log.ClientIpAddress
            })
            .ToListAsync(cancellationToken);

        if (filas.Count == 0)
        {
            return new List<EntityChangeEntry>();
        }

        // Las propiedades en UNA segunda consulta, no como Include: un Include multiplicaría
        // cada fila de cambio por sus columnas y luego habría que deduplicar en memoria.
        var ids = filas.Select(f => f.Id).ToList();

        var propiedades = await dbContext.Set<EntityPropertyChange>()
            .Where(p => ids.Contains(p.EntityChangeId))
            .Select(p => new
            {
                p.EntityChangeId,
                p.PropertyName,
                p.OriginalValue,
                p.NewValue
            })
            .ToListAsync(cancellationToken);

        var porCambio = propiedades
            .GroupBy(p => p.EntityChangeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return filas.Select(f => new EntityChangeEntry
        {
            Id = f.Id,
            ChangeTime = f.ChangeTime,
            ChangeType = f.ChangeType.ToString(),
            EntityTypeFullName = f.EntityTypeFullName,
            EntityId = f.EntityId,
            UserName = f.UserName,
            ClientIpAddress = f.ClientIpAddress,
            Properties = porCambio.TryGetValue(f.Id, out var props)
                ? props
                    .OrderBy(p => p.PropertyName)
                    .Select(p => new PropertyChangeEntry
                    {
                        PropertyName = p.PropertyName,
                        OriginalValue = p.OriginalValue,
                        NewValue = p.NewValue
                    })
                    .ToList()
                : new List<PropertyChangeEntry>()
        }).ToList();
    }

    public async Task<long> GetCountAsync(
        DateTime sinceUtc,
        string? entityTypeFullName = null,
        string? entityId = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var consulta =
            from cambio in Filtrar(dbContext, sinceUtc, entityTypeFullName, entityId)
            join log in dbContext.Set<AuditLog>() on cambio.AuditLogId equals log.Id
            select new { cambio, log };

        if (!string.IsNullOrWhiteSpace(userName))
        {
            var buscado = userName.Trim();
            consulta = consulta.Where(x =>
                x.log.UserName != null && EF.Functions.ILike(x.log.UserName, "%" + buscado + "%"));
        }

        return await consulta.LongCountAsync(cancellationToken);
    }

    public async Task<List<string>> GetTrackedTypesAsync(
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        return await dbContext.Set<EntityChange>()
            .Select(c => c.EntityTypeFullName)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// El filtro común de las dos consultas. Igual que con el informe de productos: si el
    /// <c>WHERE</c> de la lista y el del total se separan, la paginación miente sin dar
    /// ningún error. Aquí se comparte el método en vez de duplicar el SQL.
    /// </summary>
    private static IQueryable<EntityChange> Filtrar(
        IgasConsoleDbContext dbContext, DateTime sinceUtc, string? tipo, string? entityId)
    {
        var consulta = dbContext.Set<EntityChange>()
            .Where(c => c.ChangeTime >= sinceUtc);

        if (!string.IsNullOrWhiteSpace(tipo))
        {
            consulta = consulta.Where(c => c.EntityTypeFullName == tipo);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            // Exacto: se llega aquí pegando el id de una fila concreta para ver su historia.
            consulta = consulta.Where(c => c.EntityId == entityId);
        }

        return consulta;
    }
}
