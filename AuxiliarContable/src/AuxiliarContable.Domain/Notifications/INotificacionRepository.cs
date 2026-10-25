using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Notifications;

/// <summary>Un aviso junto con si QUIEN pregunta ya lo ha leído.</summary>
public class NotificacionConEstado
{
    public Notificacion Notificacion { get; set; } = null!;

    public bool Leida { get; set; }
}

public interface INotificacionRepository
{
    Task InsertAsync(Notificacion notificacion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Avisos visibles para este usuario: los suyos personales más los dirigidos a
    /// cualquiera de los permisos que tiene.
    /// <para>Los permisos se pasan ya resueltos: quién los tiene lo decide ABP, y meter esa
    /// consulta aquí ataría el repositorio al sistema de autorización.</para>
    /// </summary>
    Task<List<NotificacionConEstado>> GetForUserAsync(
        Guid userId,
        IReadOnlyCollection<string> permisos,
        bool soloNoLeidas,
        int maxResults,
        CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(
        Guid userId,
        IReadOnlyCollection<string> permisos,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca como leídos los avisos indicados que aún no lo estén para este usuario.
    /// <para>Idempotente: volver a marcar algo ya leído no crea una segunda fila ni mueve
    /// la fecha de la primera, que es la que dice cuándo se vio de verdad.</para>
    /// </summary>
    Task MarkReadAsync(
        Guid userId,
        IReadOnlyCollection<Guid> notificacionIds,
        CancellationToken cancellationToken = default);

    /// <summary>Marca todo lo visible para este usuario. Es el «limpiar» de la campana.</summary>
    Task MarkAllReadAsync(
        Guid userId,
        IReadOnlyCollection<string> permisos,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Ya se avisó de esto hace poco?
    ///
    /// <para>Es el freno que impide que un vigilante repita el mismo aviso en cada pasada
    /// hasta que deje de leerse. Se compara por título porque es lo que ve la persona: dos
    /// avisos con el mismo texto son el mismo aviso, aunque los genere otra ejecución.</para>
    ///
    /// <para>⚠️ <paramref name="userId"/> forma parte de la comparación, no es un extra. Sin
    /// él, dos personas con una clave de API llamada igual compartirían el freno: la primera
    /// recibiría su aviso y la segunda se quedaría sin el suyo para siempre. Para los avisos
    /// dirigidos a un permiso va <c>null</c>, que casa con las filas sin destinatario personal.</para>
    /// </summary>
    Task<bool> ExistsRecentAsync(
        string title,
        Guid? userId,
        DateTime desdeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Los permisos que aparecen como destinatario en los avisos vivos.
    /// <para>Sirve para preguntar a ABP solo por esos y no por el catálogo entero. La lista
    /// es corta por construcción: son los permisos que usan los pocos sitios que avisan, y
    /// la purga por retención impide que crezca con permisos que ya no se usan.</para>
    /// </summary>
    Task<List<string>> GetTargetPermissionsAsync(CancellationToken cancellationToken = default);

    Task<int> PurgeOlderThanAsync(
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default);
}
