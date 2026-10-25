using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Auditing;

/// <summary>
/// Lectura y purga de la auditoría de ABP. El módulo de audit logging trae su propio
/// repositorio, pero no una purga por retención: sin esto <c>AbpAuditLogs</c> crece para
/// siempre, y con las peticiones GET auditadas crece deprisa.
/// </summary>
public interface IAuditLogMaintenanceRepository
{
    /// <summary>Peticiones recientes, más nuevas primero. Los filtros son opcionales y
    /// se combinan entre sí (null = no filtrar por eso).</summary>
    Task<List<AuditLogEntry>> GetRecentAsync(
        DateTime sinceUtc,
        int maxResults,
        int? minStatusCode = null,
        string? ipAddress = null,
        string? userName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Borra las entradas anteriores a la fecha dada. Devuelve cuántas borró.
    /// Las tablas hijas (acciones y cambios de entidad) caen por CASCADE en la base.</summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}

/// <summary>Una petición auditada. Read model: no es la entidad de ABP, solo lo que la
/// página necesita mostrar.</summary>
public class AuditLogEntry
{
    public DateTime ExecutionTime { get; set; }
    public int ExecutionDuration { get; set; }
    public string? HttpMethod { get; set; }
    public string? Url { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ClientIpAddress { get; set; }
    public string? UserName { get; set; }
    public bool HasException { get; set; }
}
