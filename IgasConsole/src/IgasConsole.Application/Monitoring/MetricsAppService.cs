using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using IgasConsole.Exporting;
using IgasConsole.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Auditing;
using Volo.Abp.Content;

namespace IgasConsole.Monitoring;

/// <summary>
/// Lectura del histórico de métricas. Solo lee: quien escribe es el recolector
/// (MetricsCollectorWorker), que vive en el host porque depende del sistema operativo
/// y del pipeline de peticiones.
/// </summary>
[Authorize(IgasConsolePermissions.Metrics.Default)]
// La pagina /metrics sondea estos endpoints cada minuto mientras este abierta. Con los
// GET auditados, dejarlos entrar llenaria el registro de peticiones con el propio panel
// mirandose a si mismo y enterraria el trafico real. Mismo criterio que
// RequestMetricsMiddleware con /health-status.
[DisableAuditing]
public class MetricsAppService : IgasConsoleAppService, IMetricsAppService
{
    private readonly IServerMetricRepository _serverMetricRepository;
    private readonly IRateLimitEventRepository _rateLimitEventRepository;

    public MetricsAppService(
        IServerMetricRepository serverMetricRepository,
        IRateLimitEventRepository rateLimitEventRepository)
    {
        _serverMetricRepository = serverMetricRepository;
        _rateLimitEventRepository = rateLimitEventRepository;
    }

    public async Task<MetricsSeriesDto> GetSeriesAsync(GetMetricsInput input)
    {
        // Kind.Utc explícito: la columna es timestamptz y Npgsql rechaza cualquier otro
        // Kind en tiempo de ejecución, no al compilar.
        var hasta = DateTime.UtcNow;
        var desde = hasta.AddHours(-input.Hours);

        var bucket = ElegirBucket(input.Hours);

        var puntos = await _serverMetricRepository.GetSeriesAsync(desde, hasta, bucket);

        var dto = new MetricsSeriesDto
        {
            FromUtc = desde,
            ToUtc = hasta,
            BucketMinutes = bucket,
            Points = puntos.Select(Mapear).ToList()
        };

        dto.Latest = dto.Points.LastOrDefault();
        dto.SampleIntervalSeconds = MonitoringConsts.SampleIntervalSeconds;

        // La antigüedad se calcula sobre la MUESTRA REAL, no sobre el cubo agregado: un
        // cubo de 60 minutos tiene su marca al principio de la hora y haría parecer que
        // los datos llevan una hora muertos cuando acaban de llegar.
        var ultima = await _serverMetricRepository.GetLastSampleTimeAsync();
        if (ultima.HasValue)
        {
            dto.SecondsSinceLastSample = (hasta - ultima.Value).TotalSeconds;
        }

        return dto;
    }

    public async Task<List<RateLimitEventDto>> GetBlockedEventsAsync(GetRateLimitEventsInput input)
    {
        var desde = DateTime.UtcNow.AddHours(-input.Hours);

        var eventos = await _rateLimitEventRepository.GetRecentAsync(desde, input.MaxResults);

        return eventos.Select(Mapear).ToList();
    }

    public async Task<IRemoteStreamContent> GetBlockedEventsCsvAsync(GetRateLimitEventsInput input)
    {
        // Reutiliza el método de lectura: el CSV y la tabla no pueden discrepar.
        var eventos = await GetBlockedEventsAsync(input);

        var csv = EscritorCsv.Generar(
            ["Ventana (UTC)", "IP", "Endpoint", "Rechazos", "Primero", "Último"],
            eventos.Select(e => new string?[]
            {
                EscritorCsv.Fecha(e.WindowStartUtc),
                e.IpAddress,
                e.Endpoint,
                EscritorCsv.Numero(e.RejectionCount),
                EscritorCsv.Fecha(e.FirstRejectedUtc),
                EscritorCsv.Fecha(e.LastRejectedUtc)
            }));

        return new RemoteStreamContent(
            new MemoryStream(csv),
            EscritorCsv.NombreConFecha("trafico-bloqueado"),
            "text/csv");
    }

    /// <summary>
    /// El tamaño del cubo lo decide el servidor, no el cliente: así la respuesta ronda
    /// siempre los 60-170 puntos, sea cual sea la ventana. Pedir una semana con cubos de
    /// un minuto serían 10.080 puntos que ningún gráfico puede dibujar de forma útil.
    /// </summary>
    private static int ElegirBucket(int horas)
    {
        return horas switch
        {
            <= 2 => 1,
            <= 12 => 5,
            <= 48 => 15,
            _ => 60
        };
    }

    private static MetricPointDto Mapear(MetricSeriesPoint p)
    {
        return new MetricPointDto
        {
            Bucket = p.Bucket,
            CpuPercent = Redondear(p.CpuPercent),
            MemoryPercent = Redondear(p.MemoryPercent),
            DiskUsedPercent = Redondear(p.DiskUsedPercent),
            RequestCount = p.RequestCount,
            ErrorCount = p.ErrorCount,
            AvgResponseMs = Redondear(p.AvgResponseMs),
            MaxResponseMs = Redondear(p.MaxResponseMs),
            UnhealthySamples = p.UnhealthySamples,
            SwapPercent = Redondear(p.SwapPercent),
            DbSizeMb = Redondear(p.DbSizeMb),
            DbConnections = Redondear(p.DbConnections),
            ClientErrorCount = p.ClientErrorCount,
            Restarts = p.Restarts,
            RateLimitedCount = p.RateLimitedCount
        };
    }

    private static RateLimitEventDto Mapear(RateLimitEvent e)
    {
        return new RateLimitEventDto
        {
            WindowStartUtc = e.WindowStartUtc,
            IpAddress = e.IpAddress,
            Endpoint = e.Endpoint,
            RejectionCount = e.RejectionCount,
            FirstRejectedUtc = e.FirstRejectedUtc,
            LastRejectedUtc = e.LastRejectedUtc
        };
    }

    private static double? Redondear(double? valor)
    {
        return valor.HasValue ? Math.Round(valor.Value, 2) : null;
    }
}
