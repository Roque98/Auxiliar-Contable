using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AuxiliarContable.Notifications;

/// <summary>
/// «Esta persona ya vio este aviso.»
///
/// <para>Es una tabla aparte porque un aviso dirigido a un permiso lo ven varias personas,
/// y cada una lo lee cuando le toca. Una columna <c>IsRead</c> en la notificación
/// significaría «alguien lo leyó», que no sirve para nada: el contador de la campana
/// dejaría de contar en cuanto lo abriera el primero.</para>
///
/// <para>Las filas se crean **al leer**, no al enviar. Así un aviso a un permiso no escribe
/// nada por cada administrador que exista —ni deja de funcionar para quien llegue después—.
/// «No leído» es la ausencia de fila.</para>
/// </summary>
public class NotificacionLeida : Entity<Guid>
{
    public Guid NotificacionId { get; protected set; }

    public Guid UserId { get; protected set; }

    public DateTime ReadUtc { get; protected set; }

    protected NotificacionLeida()
    {
    }

    public NotificacionLeida(Guid id, Guid notificacionId, Guid userId, DateTime readUtc)
        : base(id)
    {
        if (readUtc.Kind != DateTimeKind.Utc)
        {
            throw new AbpException(
                "NotificacionLeida.ReadUtc debe ser UTC: la columna es timestamptz y Npgsql " +
                "lanza en tiempo de ejecución si el Kind no es Utc.");
        }

        NotificacionId = notificacionId;
        UserId = userId;
        ReadUtc = readUtc;
    }
}
