using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;

namespace IgasConsole.Notifications;

/// <summary>
/// La campana y su pantalla.
///
/// <para>Sin permiso propio: cada uno ve lo suyo. Lo que decide qué se ve es el cruce entre
/// el usuario actual y los permisos que tiene — nunca un parámetro de la petición.</para>
/// </summary>
[Authorize]
public class NotificationAppService : IgasConsoleAppService, INotificationAppService
{
    private readonly INotificacionRepository _repositorio;
    private readonly IPermissionChecker _permisos;

    public NotificationAppService(
        INotificacionRepository repositorio,
        IPermissionChecker permisos)
    {
        _repositorio = repositorio;
        _permisos = permisos;
    }

    public async Task<List<NotificationDto>> GetListAsync(GetNotificationsInput input)
    {
        var usuario = UsuarioActual();
        var permisos = await PermisosConcedidosAsync();

        var filas = await _repositorio.GetForUserAsync(
            usuario, permisos, input.OnlyUnread, input.MaxResults);

        return filas.Select(Mapear).ToList();
    }

    public async Task<int> GetUnreadCountAsync()
    {
        var usuario = UsuarioActual();
        var permisos = await PermisosConcedidosAsync();

        return await _repositorio.CountUnreadAsync(usuario, permisos);
    }

    public async Task MarkReadAsync(MarkReadInput input)
    {
        if (input.Ids.Count == 0)
        {
            return;
        }

        // ⚠️ Se filtra por lo que el usuario PUEDE ver antes de marcar. Sin esto, mandar
        // ids ajenos crearía filas de lectura sobre avisos que no le corresponden: no
        // llegaría a leerlos, pero sí ensuciaría la tabla con adivinanzas.
        var usuario = UsuarioActual();
        var permisos = await PermisosConcedidosAsync();

        var visibles = await _repositorio.GetForUserAsync(
            usuario, permisos, soloNoLeidas: false, maxResults: NotificationConsts.MaxResults);

        var permitidos = visibles
            .Select(f => f.Notificacion.Id)
            .Intersect(input.Ids)
            .ToList();

        await _repositorio.MarkReadAsync(usuario, permitidos);
    }

    public async Task MarkAllReadAsync()
    {
        var usuario = UsuarioActual();
        var permisos = await PermisosConcedidosAsync();

        await _repositorio.MarkAllReadAsync(usuario, permisos);
    }

    /// <summary>
    /// De qué permisos-destinatario dispone quien pregunta.
    /// <para>Se pregunta solo por los que de hecho aparecen en avisos vivos, no por el
    /// catálogo entero: son unos pocos y ABP cachea la comprobación durante la petición.</para>
    /// </summary>
    private async Task<List<string>> PermisosConcedidosAsync()
    {
        var candidatos = await _repositorio.GetTargetPermissionsAsync();

        var concedidos = new List<string>();

        foreach (var permiso in candidatos)
        {
            if (await _permisos.IsGrantedAsync(permiso))
            {
                concedidos.Add(permiso);
            }
        }

        return concedidos;
    }

    private Guid UsuarioActual()
    {
        // [Authorize] ya lo garantiza; esto convierte un fallo de configuración en un
        // mensaje claro en vez de en un Guid vacío que consultaría los avisos de nadie.
        return CurrentUser.Id
            ?? throw new AbpException("No hay usuario en la sesión al consultar los avisos.");
    }

    private static NotificationDto Mapear(NotificacionConEstado fila)
    {
        return new NotificationDto
        {
            Id = fila.Notificacion.Id,
            Severity = fila.Notificacion.Severity.ToString(),
            Title = fila.Notificacion.Title,
            Body = fila.Notificacion.Body,
            Url = fila.Notificacion.Url,
            CreationTime = fila.Notificacion.CreationTime,
            IsRead = fila.Leida
        };
    }
}
