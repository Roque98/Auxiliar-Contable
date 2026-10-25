using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;

namespace AuxiliarContable.Notifications;

public class NotificacionRepository : INotificacionRepository, ITransientDependency
{
    private readonly IDbContextProvider<AuxiliarContableDbContext> _dbContextProvider;
    private readonly IGuidGenerator _guids;

    public NotificacionRepository(
        IDbContextProvider<AuxiliarContableDbContext> dbContextProvider,
        IGuidGenerator guids)
    {
        _dbContextProvider = dbContextProvider;
        _guids = guids;
    }

    public async Task InsertAsync(
        Notificacion notificacion, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.Set<Notificacion>().AddAsync(notificacion, cancellationToken);
    }

    public async Task<List<NotificacionConEstado>> GetForUserAsync(
        Guid userId,
        IReadOnlyCollection<string> permisos,
        bool soloNoLeidas,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Se proyecta el "leída" con una subconsulta en vez de traer las dos tablas y
        // cruzarlas en memoria: así el tope de filas se aplica DESPUÉS de saber cuáles
        // están sin leer, que es lo que se pide al filtrar.
        var consulta =
            from n in Visibles(dbContext, userId, permisos)
            let leida = dbContext.Set<NotificacionLeida>()
                .Any(l => l.NotificacionId == n.Id && l.UserId == userId)
            where !soloNoLeidas || !leida
            orderby n.CreationTime descending
            select new NotificacionConEstado { Notificacion = n, Leida = leida };

        return await consulta.Take(maxResults).ToListAsync(cancellationToken);
    }

    public async Task<int> CountUnreadAsync(
        Guid userId,
        IReadOnlyCollection<string> permisos,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        return await Visibles(dbContext, userId, permisos)
            .CountAsync(
                n => !dbContext.Set<NotificacionLeida>()
                    .Any(l => l.NotificacionId == n.Id && l.UserId == userId),
                cancellationToken);
    }

    public async Task MarkReadAsync(
        Guid userId,
        IReadOnlyCollection<Guid> notificacionIds,
        CancellationToken cancellationToken = default)
    {
        if (notificacionIds.Count == 0)
        {
            return;
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Las que YA están marcadas se dejan como están: volver a marcar no puede mover la
        // fecha de la primera lectura, que es la única que dice cuándo se vio de verdad.
        var yaLeidas = await dbContext.Set<NotificacionLeida>()
            .Where(l => l.UserId == userId && notificacionIds.Contains(l.NotificacionId))
            .Select(l => l.NotificacionId)
            .ToListAsync(cancellationToken);

        var pendientes = notificacionIds.Except(yaLeidas).ToList();

        var ahora = DateTime.UtcNow;

        foreach (var id in pendientes)
        {
            await dbContext.Set<NotificacionLeida>().AddAsync(
                new NotificacionLeida(_guids.Create(), id, userId, ahora), cancellationToken);
        }
    }

    public async Task MarkAllReadAsync(
        Guid userId,
        IReadOnlyCollection<string> permisos,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var pendientes = await Visibles(dbContext, userId, permisos)
            .Where(n => !dbContext.Set<NotificacionLeida>()
                .Any(l => l.NotificacionId == n.Id && l.UserId == userId))
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        await MarkReadAsync(userId, pendientes, cancellationToken);
    }

    public async Task<bool> ExistsRecentAsync(
        string title,
        Guid? userId,
        DateTime desdeUtc,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        return await dbContext.Set<Notificacion>()
            .AnyAsync(
                n => n.Title == title
                     && n.UserId == userId
                     && n.CreationTime >= desdeUtc,
                cancellationToken);
    }

    public async Task<List<string>> GetTargetPermissionsAsync(
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        return await dbContext.Set<Notificacion>()
            .Where(n => n.RequiredPermission != null)
            .Select(n => n.RequiredPermission!)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<int> PurgeOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Las marcas de lectura primero: son hijas y su FK es ON DELETE CASCADE, pero
        // borrarlas explícitamente deja el conteo que se devuelve refiriéndose solo a
        // avisos, que es lo que se registra en el log.
        var caducadas = dbContext.Set<Notificacion>()
            .Where(n => n.CreationTime < cutoffUtc)
            .Select(n => n.Id);

        await dbContext.Set<NotificacionLeida>()
            .Where(l => caducadas.Contains(l.NotificacionId))
            .ExecuteDeleteAsync(cancellationToken);

        return await dbContext.Set<Notificacion>()
            .Where(n => n.CreationTime < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Lo que este usuario puede ver: lo suyo personal más lo dirigido a un permiso que
    /// tiene.
    /// <para>⚠️ El filtro de permisos va SIEMPRE, incluso con la lista vacía: sin él, un
    /// usuario sin ningún permiso vería todos los avisos de administración. Con la lista
    /// vacía, <c>Contains</c> no deja pasar ninguno — que es justo lo correcto.</para>
    /// <para>El filtro de tenant lo pone ABP solo: <see cref="Notificacion"/> es
    /// <c>IMultiTenant</c> y esto es LINQ, no SQL crudo.</para>
    /// </summary>
    private static IQueryable<Notificacion> Visibles(
        AuxiliarContableDbContext dbContext, Guid userId, IReadOnlyCollection<string> permisos)
    {
        var lista = permisos.ToList();

        return dbContext.Set<Notificacion>()
            .Where(n => n.UserId == userId
                || (n.RequiredPermission != null && lista.Contains(n.RequiredPermission)));
    }
}
