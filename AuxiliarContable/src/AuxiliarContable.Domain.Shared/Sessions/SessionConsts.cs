namespace AuxiliarContable.Sessions;

public static class SessionConsts
{
    /// <summary>
    /// Cada cuántos minutos, como mucho, se refresca la marca de último acceso.
    /// <para>Escribirla en cada petición convertiría toda llamada de lectura en una
    /// escritura. Con este margen la marca sigue sirviendo para lo que hace falta —saber
    /// cuál de tus sesiones es la vieja que conviene cerrar— sin castigar el camino
    /// caliente, que aquí es TODAS las peticiones autenticadas.</para>
    /// </summary>
    public const int LastAccessedRefreshMinutes = 5;

    /// <summary>
    /// Días sin actividad tras los que una sesión se borra sola.
    /// <para>Sin esto la tabla solo crece: cada login deja una fila y casi nadie cierra
    /// sesión de forma explícita, así que la lista acabaría llena de sesiones muertas de
    /// hace meses y dejaría de poder leerse.</para>
    /// </summary>
    public const int InactivityRetentionDays = 30;

    /// <summary>Cada cuánto corre la purga, en horas.</summary>
    public const int PurgeIntervalHours = 12;

    /// <summary>Identificador de cliente que se registra para los inicios de sesión web.</summary>
    public const string WebClientId = "web";
}
