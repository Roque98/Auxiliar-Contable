using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace IgasConsole.Auditing;

public class PropertyChangeDto
{
    public string PropertyName { get; set; } = string.Empty;

    public string? OriginalValue { get; set; }

    public string? NewValue { get; set; }
}

public class EntityChangeDto
{
    public Guid Id { get; set; }

    public DateTime ChangeTime { get; set; }

    /// <summary>Created | Updated | Deleted.</summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>Nombre completo del tipo, para filtrar.</summary>
    public string EntityTypeFullName { get; set; } = string.Empty;

    /// <summary>Solo la última parte del nombre: es lo que se enseña.</summary>
    public string EntityName { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>Null si el cambio ocurrió fuera de una petición (por ejemplo, un worker).</summary>
    public string? UserName { get; set; }

    public string? ClientIpAddress { get; set; }

    public List<PropertyChangeDto> Properties { get; set; } = new();
}

public class GetEntityChangesInput : PagedResultRequestDto
{
    [Range(1, 365)]
    public int Days { get; set; } = 30;

    /// <summary>Nombre completo del tipo. Vacío = todos.</summary>
    public string? EntityTypeFullName { get; set; }

    /// <summary>Id exacto: se llega aquí para ver la historia de una fila concreta.</summary>
    public string? EntityId { get; set; }

    /// <summary>Parcial y sin distinguir mayúsculas: aquí se busca a una persona.</summary>
    public string? UserName { get; set; }
}

public class EntityChangeFiltersDto
{
    /// <summary>Tipos que de verdad aparecen en el historial.</summary>
    public List<string> EntityTypes { get; set; } = new();
}

/// <summary>
/// Historial de cambios: quién cambió qué campo, de qué valor a cuál.
///
/// <para>Complementa al registro de peticiones, que dice qué endpoint se llamó pero no qué
/// se modificó. Las tablas (<c>AbpEntityChanges</c>) llevaban desde la migración inicial
/// sin nadie que las llenara — lo que faltaba era declarar qué entidades rastrear.</para>
/// </summary>
public interface IEntityChangeAppService : IApplicationService
{
    Task<PagedResultDto<EntityChangeDto>> GetListAsync(GetEntityChangesInput input);

    Task<EntityChangeFiltersDto> GetFiltersAsync();
}
