using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Auditing;

/// <summary>Un campo que cambió, con su valor anterior y el nuevo.</summary>
public class PropertyChangeEntry
{
    public string PropertyName { get; set; } = string.Empty;

    public string? OriginalValue { get; set; }

    public string? NewValue { get; set; }
}

/// <summary>
/// Un cambio sobre una entidad: qué se tocó, cuándo, quién y qué campos.
///
/// <para>Es una proyección y no la entidad de ABP porque <c>EntityChange</c> vive colgando
/// de <c>AuditLog</c>, y llegar al usuario que lo hizo obliga a cruzar las dos tablas.
/// Materializar los agregados enteros para pintar una lista traería medio registro de
/// auditoría por pantalla.</para>
/// </summary>
public class EntityChangeEntry
{
    public Guid Id { get; set; }

    public DateTime ChangeTime { get; set; }

    /// <summary>Created | Updated | Deleted.</summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>Nombre completo del tipo. La pantalla enseña solo la última parte.</summary>
    public string EntityTypeFullName { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Quién lo hizo. Sale del <c>AuditLog</c> padre, no del propio cambio.
    /// <para>Null cuando el cambio ocurrió fuera de una petición —por ejemplo desde un
    /// worker— porque entonces no hay usuario que atribuir.</para>
    /// </summary>
    public string? UserName { get; set; }

    public string? ClientIpAddress { get; set; }

    public List<PropertyChangeEntry> Properties { get; set; } = new();
}

/// <summary>
/// Consulta del historial de cambios. Solo lee: lo escribe la auditoría de ABP cuando la
/// entidad está en <c>EntityHistorySelectors</c>.
/// </summary>
public interface IEntityChangeRepository
{
    Task<List<EntityChangeEntry>> GetRecentAsync(
        DateTime sinceUtc,
        int maxResults,
        string? entityTypeFullName = null,
        string? entityId = null,
        string? userName = null,
        CancellationToken cancellationToken = default);

    Task<long> GetCountAsync(
        DateTime sinceUtc,
        string? entityTypeFullName = null,
        string? entityId = null,
        string? userName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Los tipos que de verdad aparecen en el historial, para el desplegable.
    /// <para>De los datos y no de una lista fija: la selección de entidades rastreadas
    /// puede cambiar, y una lista fija ofrecería filtros que no encuentran nada.</para>
    /// </summary>
    Task<List<string>> GetTrackedTypesAsync(CancellationToken cancellationToken = default);
}
