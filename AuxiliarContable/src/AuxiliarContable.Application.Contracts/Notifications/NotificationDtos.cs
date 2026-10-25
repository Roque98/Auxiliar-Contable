using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Notifications;

public class NotificationDto
{
    public Guid Id { get; set; }

    /// <summary>Info | Warning | Error. Texto y no número: la URL y el HTML se leen solos.</summary>
    public string Severity { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Ruta interna a la que lleva, o null.</summary>
    public string? Url { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>Leída POR QUIEN PREGUNTA. Un mismo aviso está leído para unos y no para otros.</summary>
    public bool IsRead { get; set; }
}

public class GetNotificationsInput
{
    public bool OnlyUnread { get; set; }

    [Range(1, NotificationConsts.MaxResults)]
    public int MaxResults { get; set; } = 50;
}

public class MarkReadInput
{
    /// <summary>Vacío = no marca nada. Para marcarlo todo está <c>MarkAllReadAsync</c>.</summary>
    public List<Guid> Ids { get; set; } = new();
}

/// <summary>
/// Avisos dentro de la aplicación. **Sin permiso propio**: cada usuario ve los suyos, y de
/// los dirigidos a un permiso solo llegan los de los permisos que ya tiene. Pedir un permiso
/// extra para abrir la campana solo conseguiría que alguien tuviera avisos que no puede leer.
/// </summary>
public interface INotificationAppService : IApplicationService
{
    Task<List<NotificationDto>> GetListAsync(GetNotificationsInput input);

    /// <summary>Lo que pinta el número rojo de la campana.</summary>
    Task<int> GetUnreadCountAsync();

    Task MarkReadAsync(MarkReadInput input);

    Task MarkAllReadAsync();
}
