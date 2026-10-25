using System;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace IgasConsole.Observabilidad;

/// <summary>
/// OpenTelemetry de la API: trazas (cada petición con sus consultas a PostgreSQL, sus llamadas
/// HTTP) y métricas (por ruta y del runtime), por OTLP/HTTP a
/// OpenObserve. Los LOGS van por Serilog (Program.cs), con el id de la traza. Sin
/// <c>Observabilidad:Otlp:Endpoint</c> no se registra nada: desarrollo y pruebas no exportan.
///
/// <para>Convive con las métricas propias (<c>Monitoring/</c>, pantallas /metrics y avisos por
/// Telegram): aquellas son el resumen para el equipo; esto es para diagnosticar.</para>
/// </summary>
public static class ObservabilidadServiceCollectionExtensions
{
    public static IServiceCollection AddObservabilidad(this IServiceCollection services, IConfiguration configuration, IHostEnvironment entorno)
    {
        var opciones = configuration.GetSection(ObservabilidadOptions.Seccion).Get<ObservabilidadOptions>() ?? new ObservabilidadOptions();
        if (!opciones.Otlp.Configurado)
        {
            return services;
        }

        var version = typeof(ObservabilidadServiceCollectionExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "desconocida";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(opciones.Servicio, serviceVersion: version)
                .AddAttributes([new("deployment.environment.name", entorno.EnvironmentName)]))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o =>
                {
                    o.RecordException = true;
                    o.Filter = contexto => !EsRuido(contexto.Request.Path);
                })
                .AddHttpClientInstrumentation(o => o.RecordException = true)
                .AddNpgsql()
                .AddOtlpExporter(o => Exportar(o, opciones.Otlp, "traces")))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o => Exportar(o, opciones.Otlp, "metrics")));
        return services;
    }

    /// <summary>
    /// Lo que no se traza: la salud (la piden la vigilancia y la UI de health cada pocos segundos) y
    /// los archivos estáticos. Las métricas por ruta sí los cuentan.
    /// </summary>
    private static bool EsRuido(PathString ruta) =>
        ruta.StartsWithSegments("/health-status") || ruta.StartsWithSegments("/health-ui")
        || ruta.StartsWithSegments("/healthchecks-api") || ruta.StartsWithSegments("/libs")
        || ruta.StartsWithSegments("/favicon.ico");

    private static void Exportar(OtlpExporterOptions o, OtlpOptions otlp, string senal)
    {
        o.Protocol = OtlpExportProtocol.HttpProtobuf;
        o.Endpoint = new Uri(otlp.Url(senal));
        if (!string.IsNullOrWhiteSpace(otlp.Authorization))
        {
            // Formato de cabeceras OTLP: «clave=valor». El valor (Basic + base64) puede llevar «=»:
            // el exportador corta en el PRIMER «=».
            o.Headers = "Authorization=" + otlp.Authorization.Trim();
        }
    }
}
