namespace AuxiliarContable.IA.Claude;

/// <summary>
/// Configuración de Claude (API de Anthropic), sección <c>Claude</c>. La API key NUNCA va en
/// <c>appsettings*.json</c>: en desarrollo va en user-secrets y en producción en
/// <c>/etc/auxiliar-contable/env</c> como <c>Claude__ApiKey</c>.
///
/// <para>Sin API key el modelo se da por no configurado y cada uso lanza sin llamar a nadie. Es
/// lo que pasa en los tests, a propósito: un test no debe gastar dinero.</para>
/// </summary>
public class ClaudeOptions
{
    public const string Seccion = "Claude";

    public string BaseUrl { get; set; } = "https://api.anthropic.com/";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Haiku: el más barato y rápido, y lee imágenes y PDF. Para extraer datos con esquema fijo
    /// basta; si algún tipo de documento no sale bien, se sube el modelo SOLO para ese propósito
    /// antes que para todos.
    /// </summary>
    public string Modelo { get; set; } = "claude-haiku-5-5";

    /// <summary>Tope de tokens de respuesta por defecto. Un estado de cuenta de muchos movimientos puede necesitar más.</summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>
    /// Modelo del chat (cliente y despacho). Null = <see cref="Modelo"/>. Va aparte porque el chat
    /// es lo que más se usa: si Haiku se queda corto conversando, se sube solo aquí.
    /// </summary>
    public string? ModeloDelChat { get; set; }

    /// <summary>Tope de tokens de cada turno del chat: respuestas cortas, como en una plática.</summary>
    public int MaxTokensDelChat { get; set; } = 2048;

    /// <summary>Un PDF de varias páginas tarda; por debajo de un minuto se cortaban respuestas válidas.</summary>
    public int TimeoutSegundos { get; set; } = 180;

    /// <summary>Reintentos ante 429/5xx/529. Solo esos: un 400 fallaría igual.</summary>
    public int Reintentos { get; set; } = 3;

    public bool TieneCredenciales => !string.IsNullOrWhiteSpace(ApiKey);
}
