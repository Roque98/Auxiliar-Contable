namespace AuxiliarContable.Jobs;

/// <summary>
/// Constantes del panel de trabajos en segundo plano.
/// </summary>
public static class JobConsts
{
    /// <summary>Nombre del tipo de la excepción (<c>ex.GetType().FullName</c>).</summary>
    public const int MaxExceptionTypeLength = 256;

    /// <summary>
    /// Nombre del trabajo. Es el mismo tope que usa ABP en su propia tabla
    /// (<c>BackgroundJobRecordConsts.MaxJobNameLength</c>, verificado en <c>refs/abp</c>):
    /// esta columna copia aquel valor, así que un tope menor lo truncaría sin motivo.
    /// </summary>
    public const int MaxJobNameLength = 128;

    /// <summary>
    /// Mensaje de la excepción. Se trunca a propósito: un mensaje puede traer un volcado
    /// entero (una consulta SQL fallida, una respuesta HTTP completa) y esta tabla se
    /// escribe justo cuando algo va mal de forma repetida, que es cuando menos conviene
    /// que una fila crezca sin límite.
    /// </summary>
    public const int MaxMessageLength = 2048;

    /// <summary>
    /// Pila de llamadas. Cabe la parte de arriba, que es la que dice dónde falló; el resto
    /// es el andamiaje del framework y ya está en los logs.
    /// </summary>
    public const int MaxStackTraceLength = 4096;

    /// <summary>
    /// Cuánto se guarda el historial de fallos. Más largo que la vida del propio trabajo
    /// (ABP lo abandona a los 2 días) porque la pregunta que se hace aquí no es «¿qué está
    /// fallando ahora?» sino «¿esto ya había pasado antes?».
    /// </summary>
    public const int FailureRetentionDays = 30;

    /// <summary>Cada cuánto corre la purga del historial.</summary>
    public const int PurgeIntervalHours = 12;

    /// <summary>
    /// Cuánto se conserva un trabajo COMPLETADO con éxito.
    /// <para>Es corto porque los completados solo sirven para ver que el sistema está vivo;
    /// lo que interesa conservar es lo que falló, y eso vive en la tabla de fallos.</para>
    /// </summary>
    public const int SuccessfulJobRetentionDays = 3;

    /// <summary>Tope de filas que devuelve el panel de una vez.</summary>
    public const int MaxResults = 200;
}
