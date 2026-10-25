namespace IgasConsole.Observabilidad;

/// <summary>
/// A dónde manda la API sus trazas, métricas y logs (OpenTelemetry, OTLP por HTTP). En producción
/// es OpenObserve en el mismo VPS (<c>/opt/openobserve</c>); ver docs/observabilidad.md.
/// <c>Observabilidad__Otlp__*</c> en <c>/etc/zia-console/env</c>. Sin <see cref="OtlpOptions.Endpoint"/>
/// no se exporta nada (desarrollo y pruebas).
/// </summary>
public class ObservabilidadOptions
{
    public const string Seccion = "Observabilidad";

    /// <summary>El nombre del servicio en las trazas (<c>service.name</c>).</summary>
    public string Servicio { get; set; } = "zia-console-api";

    public OtlpOptions Otlp { get; set; } = new();
}

public class OtlpOptions
{
    /// <summary>La base OTLP de la organización: <c>http://127.0.0.1:5080/api/default</c> (se le añade <c>/v1/traces</c>…).</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>La cabecera <c>Authorization</c> completa (<c>Basic …</c>). Secreto: solo en el entorno.</summary>
    public string Authorization { get; set; } = "";

    public bool Configurado => !string.IsNullOrWhiteSpace(Endpoint);

    public string Url(string senal) => Endpoint.Trim().TrimEnd('/') + "/v1/" + senal;
}
