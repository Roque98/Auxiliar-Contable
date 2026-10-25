namespace AuxiliarContable.Notifications;

/// <summary>
/// Gravedad de un aviso. Decide el color y, sobre todo, si merece mirarse ahora.
/// <para>Vive en Domain.Shared porque la usan la entidad, los DTOs y la pantalla.</para>
/// </summary>
public enum NotificationSeverity
{
    /// <summary>Ha pasado algo que conviene saber, pero nada está mal.</summary>
    Info = 0,

    /// <summary>Algo va camino de romperse, o se ha degradado.</summary>
    Warning = 1,

    /// <summary>Algo está roto ahora mismo.</summary>
    Error = 2
}

public static class NotificationConsts
{
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Cuerpo del aviso. Corto a propósito: esto es una campana, no un informe. Lo que no
    /// quepa va en la pantalla a la que apunta <c>Url</c>.
    /// </summary>
    public const int MaxBodyLength = 2000;

    /// <summary>Ruta interna a la que lleva el aviso (<c>/jobs</c>, <c>/security</c>…).</summary>
    public const int MaxUrlLength = 256;

    /// <summary>Nombre de permiso, cuando el aviso va dirigido a quien lo tenga.</summary>
    public const int MaxPermissionLength = 128;

    /// <summary>
    /// Cuánto se conservan los avisos antes de purgarlos.
    /// <para>Treinta días: pasado ese tiempo, un aviso operativo o se atendió o dejó de
    /// importar, y una campana con meses de historia deja de leerse.</para>
    /// </summary>
    public const int RetentionDays = 30;

    public const int PurgeIntervalHours = 12;

    /// <summary>Tope de avisos que devuelve la pantalla de una vez.</summary>
    public const int MaxResults = 100;

    /// <summary>Cuántos se enseñan en el desplegable de la campana.</summary>
    public const int MaxEnCampana = 8;
}
