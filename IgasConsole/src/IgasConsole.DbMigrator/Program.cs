using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace IgasConsole.DbMigrator;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Volo.Abp", LogEventLevel.Warning)
#if DEBUG
                .MinimumLevel.Override("IgasConsole", LogEventLevel.Debug)
#else
                .MinimumLevel.Override("IgasConsole", LogEventLevel.Information)
#endif
                .Enrich.FromLogContext()
            // Rota igual que el del Host (ver `LogConsts`). Este escribe mucho menos —corre
            // una vez por despliegue— pero sin rotación su archivo tampoco se limpiaría
            // nunca, y es el que guarda qué migración se aplicó y cuándo.
            .WriteTo.Async(c => c.File(
                "Logs/logs.txt",
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: LogConsts.TamanoMaximoPorArchivo,
                retainedFileTimeLimit: LogConsts.Retencion,
                retainedFileCountLimit: LogConsts.MaximoDeArchivos))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        await CreateHostBuilder(args).RunConsoleAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .AddAppSettingsSecretsJson()
            .ConfigureLogging((context, logging) => logging.ClearProviders())
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<DbMigratorHostedService>();
            });
}
