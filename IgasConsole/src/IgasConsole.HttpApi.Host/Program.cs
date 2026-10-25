using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IgasConsole.Observabilidad;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace IgasConsole;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        // ⚠️ El logger de ARRANQUE también rota, y no es un detalle. Vive solo hasta que
        // `UseSerilog` lo sustituye por el configurado en appsettings, pero escribe en el
        // MISMO archivo; sin estas opciones dejaría un `logs.txt` plano que no rota nunca
        // junto a los `logs20260828.txt` que sí lo hacen, y ese archivo crecería para
        // siempre con una línea por arranque. Los valores están duplicados a propósito:
        // aquí todavía no hay configuración cargada que leer.
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(c => c.File(
                "Logs/logs.txt",
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: LogConsts.TamanoMaximoPorArchivo,
                retainedFileTimeLimit: LogConsts.Retencion,
                retainedFileCountLimit: LogConsts.MaximoDeArchivos))
            .WriteTo.Async(c => c.Console())
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting IgasConsole.HttpApi.Host.");
            var builder = WebApplication.CreateBuilder(args);
            builder.Host
                .AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .ReadFrom.Configuration(context.Configuration)
                        .ReadFrom.Services(services)
                        .WriteTo.Async(c => c.AbpStudio(services));

                    // Los logs también a OpenObserve, con el id de la traza (lo toma de la
                    // Activity en curso). El archivo sigue: si OpenObserve se cae, queda ahí.
                    var otlp = context.Configuration.GetSection(ObservabilidadOptions.Seccion).Get<ObservabilidadOptions>();
                    if (otlp?.Otlp.Configurado == true)
                    {
                        loggerConfiguration.WriteTo.OpenTelemetry(o =>
                        {
                            o.Endpoint = otlp.Otlp.Url("logs");
                            o.Protocol = OtlpProtocol.HttpProtobuf;
                            if (!string.IsNullOrWhiteSpace(otlp.Otlp.Authorization))
                            {
                                o.Headers = new Dictionary<string, string> { ["Authorization"] = otlp.Otlp.Authorization.Trim() };
                            }

                            o.ResourceAttributes = new Dictionary<string, object>
                            {
                                ["service.name"] = otlp.Servicio,
                                ["deployment.environment.name"] = context.HostingEnvironment.EnvironmentName,
                            };
                        });
                    }
                });
            await builder.AddApplicationAsync<IgasConsoleHttpApiHostModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
