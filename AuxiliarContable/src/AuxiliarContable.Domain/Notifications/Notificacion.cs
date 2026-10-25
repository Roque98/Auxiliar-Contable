using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace AuxiliarContable.Notifications;

/// <summary>
/// Un aviso dentro de la aplicación.
///
/// <para>ABP open source **no trae sistema de notificaciones** (es de Pro), así que esto
/// está hecho a mano. Hasta ahora todo aviso salía por Telegram, que solo tiene quien haya
/// vinculado su cuenta: si nadie la vinculó, el aviso no existía para nadie.</para>
///
/// <para>⚠️ **Se guarda UNA fila por aviso, no una por destinatario.** Repartirlo obligaría
/// a enumerar en el momento del envío a todos los usuarios que tienen cierto permiso —lo
/// que en ABP significa cruzar concesiones por rol y directas— y dejaría fuera a quien se
/// nombre administrador mañana, que es justo quien más necesita ver lo que pasó. Aquí el
/// aviso apunta a un **permiso** y lo ve quien lo tenga, ahora o después.</para>
///
/// <para>El precio de esa decisión es que «leído» no puede ser una columna: es de cada
/// persona. Vive en <see cref="NotificacionLeida"/>.</para>
///
/// <para>Sin soft delete: la purga por retención borra de verdad.</para>
/// </summary>
public class Notificacion : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// Destinatario personal. Null si el aviso va dirigido a un permiso.
    /// <para>Exactamente uno de <see cref="UserId"/> y <see cref="RequiredPermission"/>
    /// tiene valor; lo garantiza el constructor.</para>
    /// </summary>
    public Guid? UserId { get; protected set; }

    /// <summary>Permiso que hay que tener para verlo. Null si es personal.</summary>
    public string? RequiredPermission { get; protected set; }

    public NotificationSeverity Severity { get; protected set; }

    public string Title { get; protected set; }

    public string Body { get; protected set; }

    /// <summary>
    /// Ruta interna a la que lleva («/jobs», «/security»). Null si no hay dónde ir.
    /// <para>Es lo que convierte un aviso en algo accionable: sin enlace, «hay trabajos
    /// abandonados» obliga a recordar dónde se miran.</para>
    /// </summary>
    public string? Url { get; protected set; }

    public DateTime CreationTime { get; protected set; }

    protected Notificacion()
    {
        Title = string.Empty;
        Body = string.Empty;
    }

    public Notificacion(
        Guid id,
        Guid? tenantId,
        Guid? userId,
        string? requiredPermission,
        NotificationSeverity severity,
        string title,
        string body,
        string? url,
        DateTime creationTime)
        : base(id)
    {
        if ((userId == null) == string.IsNullOrWhiteSpace(requiredPermission))
        {
            // Sin destinatario no lo vería nadie; con los dos, no estaría claro si el
            // permiso restringe al usuario o lo amplía. Las dos formas de equivocarse
            // producen un aviso que parece enviado y no lo está.
            throw new AbpException(
                "Una notificación va dirigida O a un usuario O a un permiso, nunca a los " +
                "dos ni a ninguno.");
        }

        if (creationTime.Kind != DateTimeKind.Utc)
        {
            throw new AbpException(
                "Notificacion.CreationTime debe ser UTC: la columna es timestamptz y Npgsql " +
                "lanza en tiempo de ejecución si el Kind no es Utc.");
        }

        TenantId = tenantId;
        UserId = userId;
        RequiredPermission = requiredPermission;
        Severity = severity;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), NotificationConsts.MaxTitleLength);
        Body = Recortar(body, NotificationConsts.MaxBodyLength) ?? string.Empty;
        Url = Recortar(url, NotificationConsts.MaxUrlLength);
        CreationTime = creationTime;
    }

    /// <summary>
    /// El cuerpo se RECORTA en vez de rechazarse. Un aviso nace casi siempre a raíz de que
    /// algo va mal, y llevando dentro el mensaje de una excepción —que puede traer una
    /// consulta entera—. Reventar aquí convertiría «algo falló» en «algo falló y además no
    /// se avisó».
    /// </summary>
    private static string? Recortar(string? texto, int maximo)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto;
        }

        return texto.Length <= maximo ? texto : texto[..maximo];
    }
}
