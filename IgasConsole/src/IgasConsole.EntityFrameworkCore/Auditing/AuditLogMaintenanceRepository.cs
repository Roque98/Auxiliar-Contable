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

public class AuditLogMaintenanceRepository : IAuditLogMaintenanceRepository, ITransientDependency
{
    private readonly IDbContextProvider<IgasConsoleDbContext> _dbContextProvider;

    public AuditLogMaintenanceRepository(IDbContextProvider<IgasConsoleDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<List<AuditLogEntry>> GetRecentAsync(
        DateTime sinceUtc,
        int maxResults,
        int? minStatusCode = null,
        string? ipAddress = null,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // El rango de fechas va SIEMPRE y primero: es la unica columna indexada de las
        // que se filtran aqui (IX_AbpAuditLogs_TenantId_ExecutionTime), asi que es la
        // que evita recorrer la tabla entera. Los demas filtros se aplican sobre ese
        // subconjunto ya reducido, que con la purga por retencion se mantiene pequeno.
        var consulta = dbContext.Set<AuditLog>()
            .Where(a => a.ExecutionTime >= sinceUtc);

        // Para mirar solo lo que fallo (4xx/5xx) sin traerse el trafico normal.
        if (minStatusCode.HasValue)
        {
            consulta = consulta.Where(a => a.HttpStatusCode >= minStatusCode.Value);
        }

        // Exacta: se usa para pegar una IP del panel de trafico bloqueado y ver que mas
        // hizo. Una busqueda parcial daria falsos positivos entre direcciones distintas
        // ("1.2.3.4" aparece dentro de "11.2.3.45").
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var ip = ipAddress.Trim();
            consulta = consulta.Where(a => a.ClientIpAddress == ip);
        }

        // Parcial y sin distinguir mayusculas: aqui se busca a una persona, no una clave.
        if (!string.IsNullOrWhiteSpace(userName))
        {
            var usuario = userName.Trim();
            consulta = consulta.Where(a =>
                a.UserName != null && EF.Functions.ILike(a.UserName, "%" + usuario + "%"));
        }

        // Proyeccion a un read model: AuditLog arrastra Actions y EntityChanges, y
        // materializar el agregado entero para pintar una tabla seria traerse media
        // base de datos por pantalla.
        return await consulta
            .OrderByDescending(a => a.ExecutionTime)
            .Take(maxResults)
            .Select(a => new AuditLogEntry
            {
                ExecutionTime = a.ExecutionTime,
                ExecutionDuration = a.ExecutionDuration,
                HttpMethod = a.HttpMethod,
                Url = a.Url,
                HttpStatusCode = a.HttpStatusCode,
                ClientIpAddress = a.ClientIpAddress,
                UserName = a.UserName,
                HasException = a.Exceptions != null && a.Exceptions != ""
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<int> PurgeOlderThanAsync(
        DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        // Solo el padre: las FK de AbpAuditLogActions, AbpEntityChanges y
        // AbpEntityPropertyChanges son ON DELETE CASCADE (verificado en la base), asi
        // que la propia PostgreSQL se lleva hijos y nietos.
        return await dbContext.Set<AuditLog>()
            .Where(a => a.ExecutionTime < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
