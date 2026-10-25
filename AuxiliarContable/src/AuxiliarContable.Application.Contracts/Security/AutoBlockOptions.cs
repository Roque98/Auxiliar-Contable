using System.Collections.Generic;

namespace AuxiliarContable.Security;

/// <summary>
/// Reglas del bloqueo automático. Se leen de <c>appsettings:Security:AutoBlock</c> — no de
/// ajustes editables desde la web — por lo mismo que el rate limiting: cambiar cuándo se
/// veta a alguien no debería poder hacerse desde una pantalla sin dejar rastro en el
/// repositorio.
/// </summary>
public class AutoBlockOptions
{
    /// <summary>
    /// Apagado por defecto. Encenderlo es una decisión: a partir de ahí la aplicación
    /// empieza a dejar gente fuera sola, y eso hay que quererlo explícitamente.
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>Rechazos del rate limiter que debe acumular una IP para merecer bloqueo.</summary>
    public int RejectionThreshold { get; set; } = 300;

    /// <summary>Ventana en la que se cuentan esos rechazos, en minutos.</summary>
    public int WindowMinutes { get; set; } = 10;

    /// <summary>Cuánto dura el bloqueo automático, en horas. Siempre tiene fin: un veto
    /// automático indefinido convierte un error de umbral en un problema permanente.</summary>
    public int BlockHours { get; set; } = 24;

    /// <summary>
    /// Máximo de IPs que se bloquean en una misma evaluación.
    /// <para>Es un cortafuegos contra el propio mecanismo: ante un ataque distribuido desde
    /// cientos de direcciones, sin este tope una sola pasada llenaría la tabla y el panel
    /// dejaría de ser legible justo cuando más falta hace.</para>
    /// </summary>
    public int MaxBlocksPerRun { get; set; } = 20;

    /// <summary>
    /// Direcciones que NUNCA se bloquean automáticamente.
    /// <para>El loopback va aquí de serie: por ahí entran los health checks y los sondeos
    /// internos, y bloquearlo dejaría la aplicación marcada como caída por sí misma. Añade
    /// aquí la IP de tu oficina si no quieres que un script tuyo te deje fuera.</para>
    /// </summary>
    public List<string> NeverBlock { get; set; } = new() { "127.0.0.1", "::1" };

    /// <summary>
    /// Cuántas peticiones fallidas en la ventana hacen sospechosa a una IP que sondea.
    ///
    /// <para>Los datos reales de producción: los escaneadores medidos hacían entre 83 y 2490
    /// peticiones fallidas, y el cliente legítimo más activo hacía 3. El umbral está lejos de
    /// ambos extremos a propósito — no hay ninguna zona gris que afinar—, y por eso se puede
    /// dejar bajo: <c>20</c> sigue siendo 4× el peor caso legítimo conocido, y cuanto antes
    /// se alcanza menos alcanza a probar el escáner antes del corte. La condición que de
    /// verdad separa a un escáner de un cliente activo con enlaces rotos es
    /// <see cref="ScanMinFailureRate"/>, no este recuento.</para>
    /// </summary>
    public int ScanFailureThreshold { get; set; } = 20;

    /// <summary>
    /// Qué porcentaje de sus peticiones tiene que estar fallando.
    ///
    /// <para>Es la condición que separa a un escaneador de un cliente muy activo con algún
    /// enlace roto, y en los datos reales la separación es enorme: los escaneadores
    /// fallaban entre el 92 % y el 100 %; los usuarios de verdad, entre el 0 % y el 2 %.
    /// Sin esta condición, un cliente que hace miles de peticiones legítimas acabaría
    /// bloqueado por acumular unos pocos 404.</para>
    /// </summary>
    public int ScanMinFailureRate { get; set; } = 80;

    /// <summary>
    /// Interruptor propio para la señal de sondeo.
    /// <para>Aparte del general porque son detecciones distintas: una mira el volumen
    /// (rechazos del rate limiter) y otra el comportamiento (probar rutas que no existen).
    /// Poder apagar una sin la otra evita quedarse sin ninguna al ajustar un umbral.</para>
    /// </summary>
    public bool ScanDetectionEnabled { get; set; } = true;
}
