namespace AuxiliarContable.IA.Gateway;

/// <summary>
/// claude-gateway: la API interna del VPS (127.0.0.1:5050, nunca pública) que corre <c>claude -p</c> con la suscripción
/// de claude.ai. Sección <c>ClaudeGateway</c>. La clave (<c>cgw_…</c>, una por programa) NUNCA va en appsettings: en
/// producción va en <c>/etc/auxiliar-contable/env</c> como <c>ClaudeGateway__Clave</c>.
///
/// <para>Con clave, el CHAT del asistente usa el gateway en lugar de la API de Anthropic (que sigue para leer
/// documentos si tiene su propia clave). Sin clave no cambia nada.</para>
/// </summary>
public class ClaudeGatewayOptions
{
    public const string Seccion = "ClaudeGateway";

    public string Url { get; set; } = "http://127.0.0.1:5050/";

    public string? Clave { get; set; }

    /// <summary>haiku, sonnet, opus o un id claude-…; el chat razona con herramientas: sonnet por defecto.</summary>
    public string Modelo { get; set; } = "sonnet";

    /// <summary>Lo que espera el gateway por respuesta (tope del servicio: 900).</summary>
    public int TimeoutSegundos { get; set; } = 240;

    /// <summary>Reintentos ante 429 (cola llena), 502 y 504. Los demás fallarían igual.</summary>
    public int Reintentos { get; set; } = 2;

    public bool TieneCredenciales => !string.IsNullOrWhiteSpace(Clave);
}
