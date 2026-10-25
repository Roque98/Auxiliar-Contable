namespace AuxiliarContable.Security;

/// <summary>
/// Constantes del bloqueo de IPs.
///
/// <para>Tiene su propia longitud de IP en vez de reutilizar la de <c>MonitoringConsts</c>
/// a propósito: son dos módulos independientes, y compartir una constante entre ellos los
/// ataría sin que ninguna regla de negocio lo pida. Que el número coincida es casualidad —
/// lo que mide una IPv6 en texto — y no un acoplamiento.</para>
/// </summary>
public static class SecurityConsts
{
    /// <summary>Longitud máxima de una IPv6 en texto (con zona incluida de sobra).</summary>
    public const int MaxIpAddressLength = 45;

    /// <summary>Motivo por el que se bloqueó una IP.</summary>
    public const int MaxBlockReasonLength = 256;

    /// <summary>
    /// Cada cuánto relee el middleware la lista de IPs bloqueadas.
    /// <para>El bloqueo se comprueba en CADA petición, así que no puede consultar la base:
    /// se trabaja sobre una copia en memoria que se refresca con esta frecuencia. Al
    /// bloquear desde el panel se refresca al instante, así que en la práctica es inmediato;
    /// esto cubre los bloqueos que caducan solos y la primera carga tras un despliegue.</para>
    /// </summary>
    public const int BlockedIpsRefreshSeconds = 30;

    /// <summary>
    /// Cada cuánto se evalúa la señal de VOLUMEN (rechazos del rate limiter).
    /// <para>Un minuto, el mismo ritmo al que el recolector agrega los rechazos:
    /// <c>RateLimitEvent</c> es un rollup por IP, endpoint y minuto, así que evaluar más a
    /// menudo releería los mismos datos sin nada nuevo que ver.</para>
    /// </summary>
    public const int AutoBlockEvaluationSeconds = 60;

    /// <summary>
    /// Cada cuánto se evalúa la señal de COMPORTAMIENTO (sondeo de rutas).
    /// <para>Más corto que el de volumen a propósito: esta señal sale de <c>AbpAuditLogs</c>,
    /// que tiene una fila por PETICIÓN —no un rollup por minuto—, así que datos nuevos llegan
    /// continuamente y esperar un minuto entero solo alarga lo que un escáner alcanza a
    /// probar antes del corte. El coste es un <c>GROUP BY</c> más frecuente sobre
    /// <c>AbpAuditLogs</c>, acotado porque filtra primero por <c>ExecutionTime</c>, que está
    /// indexado.</para>
    /// </summary>
    public const int AutoBlockScanEvaluationSeconds = 20;
}
