using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Auditing;
using AuxiliarContable.BackgroundJobs;
using AuxiliarContable.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Guids;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

using AuxiliarContable.Notifications;
using AuxiliarContable.Permissions;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Toma una muestra por minuto, la guarda, evalúa los avisos y purga el histórico viejo.
/// <para>Vive en el host y no en el dominio porque depende de dos cosas que son del host:
/// el sistema operativo (/proc) y el pipeline de peticiones de ASP.NET Core.</para>
/// <para>Se registra SOLO desde el módulo del host, así que el DbMigrator —que también
/// carga el módulo de dominio— no lo arranca.</para>
/// </summary>
public class MetricsCollectorWorker : AsyncPeriodicBackgroundWorkerBase
{
    private readonly MonitoringOptions _opciones;

    /// <summary>Alertas activas. Se carga del ajuste persistido en el primer ciclo.</summary>
    private Dictionary<string, bool> _avisosActivos = new();
    private bool _estadoCargado;

    /// <summary>Muestras consecutivas fuera de rango por regla. Vive solo en memoria: tras
    /// un reinicio, lo único que pasa es que el primer aviso tarda unos minutos más.</summary>
    private readonly Dictionary<string, int> _rachas = new();

    private DateTime _ultimaPurgaUtc = DateTime.MinValue;

    public MetricsCollectorWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<MonitoringOptions> opciones)
        : base(timer, serviceScopeFactory)
    {
        _opciones = opciones.Value;
        Timer.Period = MonitoringConsts.SampleIntervalSeconds * 1000;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        if (!_opciones.IsEnabled)
        {
            return;
        }

        var sp = workerContext.ServiceProvider;
        var ct = workerContext.CancellationToken;

        var sonda = sp.GetRequiredService<ISystemMetricsProbe>();
        var acumulador = sp.GetRequiredService<IAppMetricsAccumulator>();
        var acumuladorBloqueos = sp.GetRequiredService<IRateLimitAccumulator>();
        var repositorio = sp.GetRequiredService<IServerMetricRepository>();
        var rateLimitEventRepository = sp.GetRequiredService<IRateLimitEventRepository>();
        var uowManager = sp.GetRequiredService<IUnitOfWorkManager>();
        var guids = sp.GetRequiredService<IGuidGenerator>();

        var sistema = sonda.Read();
        var peticiones = acumulador.DrainAndReset();
        var bloqueos = acumuladorBloqueos.DrainAndReset();
        var totalBloqueados = bloqueos.Sum(b => b.Count);
        var salud = await ComprobarSaludAsync(sp, ct);

        // Segundos desde que arrancó el proceso. Un valor que baja de golpe en la serie
        // delata un reinicio, que es la señal más fácil de perder mirando promedios.
        var uptime = (int)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime())
            .TotalSeconds;

        var muestra = new ServerMetric(guids.Create(), DateTime.UtcNow, salud)
            .SetSystem(sistema.CpuPercent, sistema.MemoryUsedMb, sistema.MemoryTotalMb,
                       sistema.SwapUsedMb, sistema.SwapTotalMb, sistema.DiskUsedPercent)
            .SetApplication(peticiones.RequestCount, peticiones.ErrorCount,
                            peticiones.ClientErrorCount, peticiones.AvgResponseMs,
                            peticiones.MaxResponseMs)
            .SetRateLimiting(totalBloqueados)
            .SetProcess(uptime);

        // Un worker no tiene Unit of Work ambiental (eso lo pone ABP alrededor de los
        // application services): hay que abrirlo a mano o el repositorio no tiene DbContext.
        using (var uow = uowManager.Begin(requiresNew: true, isTransactional: true))
        {
            // Dentro del UoW: necesita el DbContext. Si falla, se guarda la muestra igual
            // sin los datos de base — media métrica es mejor que ninguna.
            try
            {
                var bd = await repositorio.GetDatabaseStatsAsync(ct);
                if (bd != null)
                {
                    muestra.SetDatabase(bd.DbSizeMb, bd.DbConnections);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "No se pudieron leer las estadísticas de la base");
            }

            await repositorio.InsertAsync(muestra, autoSave: true, cancellationToken: ct);

            if (bloqueos.Count > 0)
            {
                // Mismo Timestamp que la muestra del ciclo: asi ambas tablas comparten el
                // mismo "ahora" y el panel puede cruzarlas sin ambiguedad.
                var eventos = bloqueos.Select(b => new RateLimitEvent(
                    guids.Create(), muestra.Timestamp, b.IpAddress, b.Endpoint,
                    b.Count, b.FirstOccurredUtc, b.LastOccurredUtc));

                await rateLimitEventRepository.InsertManyAsync(eventos, autoSave: true);
            }

            await PurgarSiTocaAsync(
                repositorio,
                rateLimitEventRepository,
                sp.GetRequiredService<IAuditLogMaintenanceRepository>(),
                ct);
            await uow.CompleteAsync(ct);
        }

        // El interruptor vive en los ajustes (editable desde la web), no en appsettings:
        // silenciar los avisos durante un mantenimiento no debe exigir un despliegue.
        // Se evalúa igual aunque estén apagados, para no soltar de golpe un aviso por
        // cada regla ya disparada en cuanto alguien los reactive.
        var avisosActivos = await sp.GetRequiredService<ISettingProvider>()
            .GetAsync<bool>(MonitoringSettingNames.AlertsEnabled);

        await EvaluarAvisosAsync(sp, muestra, avisosActivos, ct);
    }

    private async Task<string> ComprobarSaludAsync(IServiceProvider sp, CancellationToken ct)
    {
        // En proceso, sin pasar por HTTP: mide la salud real de la app sin depender de
        // que nginx, el DNS o el certificado estén bien.
        var servicio = sp.GetService<HealthCheckService>();
        if (servicio == null)
        {
            return MonitoringConsts.HealthUnknown;
        }

        try
        {
            var informe = await servicio.CheckHealthAsync(ct);
            return informe.Status.ToString();
        }
        catch (Exception)
        {
            return "Unhealthy";
        }
    }

    private async Task PurgarSiTocaAsync(
        IServerMetricRepository repositorio,
        IRateLimitEventRepository rateLimitEventRepository,
        IAuditLogMaintenanceRepository auditLogRepository,
        CancellationToken ct)
    {
        // Una vez al día basta: purgar cada minuto sería un DELETE inútil por minuto.
        if (_ultimaPurgaUtc.Date == DateTime.UtcNow.Date)
        {
            return;
        }

        _ultimaPurgaUtc = DateTime.UtcNow;
        var corte = DateTime.UtcNow.AddDays(-MonitoringConsts.RetentionDays);
        var borradas = await repositorio.PurgeOlderThanAsync(corte, ct);
        var eventosBorrados = await rateLimitEventRepository.PurgeOlderThanAsync(corte, ct);

        // La auditoria de ABP no trae purga propia. Con los GET auditados crece a una
        // fila por peticion, asi que sin esto llena el disco sola.
        var auditoriaBorrada = await auditLogRepository.PurgeOlderThanAsync(corte, ct);

        if (borradas > 0 || eventosBorrados > 0 || auditoriaBorrada > 0)
        {
            Logger.LogInformation(
                "Monitoreo: purgadas {Borradas} muestras, {EventosBorrados} eventos de rate limiting " +
                "y {AuditoriaBorrada} entradas de auditoria anteriores a {Corte:u}",
                borradas, eventosBorrados, auditoriaBorrada, corte);
        }
    }

    // --- Avisos -------------------------------------------------------------------

    /// <summary>
    /// Cada regla dice si está fuera de rango y cuántas muestras consecutivas hacen falta
    /// para avisar. La salud exige menos que los umbrales de recursos: un health check que
    /// falla dos minutos seguidos ya es una incidencia, mientras que la CPU al 91% durante
    /// un minuto es un backup.
    /// </summary>
    private List<(string Clave, bool Fuera, int Minimo, string Mensaje)> Reglas(ServerMetric m)
    {
        var memoriaPct = m.MemoryTotalMb > 0 && m.MemoryUsedMb.HasValue
            ? (double)m.MemoryUsedMb.Value / m.MemoryTotalMb!.Value * 100
            : (double?)null;

        var swapPct = m.SwapTotalMb > 0 && m.SwapUsedMb.HasValue
            ? (double)m.SwapUsedMb.Value / m.SwapTotalMb!.Value * 100
            : (double?)null;

        var normal = MonitoringConsts.MuestrasConsecutivasParaAvisar;

        return new List<(string, bool, int, string)>
        {
            ("disco",
             m.DiskUsedPercent >= _opciones.DiskAlertPercent,
             // El disco no se llena en un pico: si está al 85% ahora, lo estaba hace un
             // minuto. Avisar en la primera muestra da más margen de reacción.
             1,
             $"Disco al {m.DiskUsedPercent:0.#}% (umbral {_opciones.DiskAlertPercent:0.#}%)"),

            ("cpu",
             m.CpuPercent >= _opciones.CpuAlertPercent,
             normal,
             $"CPU al {m.CpuPercent:0.#}% durante {normal} minutos seguidos"),

            ("memoria",
             memoriaPct >= _opciones.MemoryAlertPercent,
             normal,
             $"Memoria al {memoriaPct:0.#}%: {m.MemoryUsedMb} MB de {m.MemoryTotalMb} MB"),

            ("swap",
             swapPct >= _opciones.SwapAlertPercent,
             normal,
             $"Swap al {swapPct:0.#}% ({m.SwapUsedMb} MB): el servidor está tirando de disco como memoria"),

            ("conexiones",
             m.DbConnections >= _opciones.DbConnectionsAlert,
             normal,
             $"{m.DbConnections} conexiones abiertas a PostgreSQL (umbral {_opciones.DbConnectionsAlert})"),

            ("salud",
             m.HealthStatus != "Healthy" && m.HealthStatus != MonitoringConsts.HealthUnknown,
             2,
             $"El health check devuelve '{m.HealthStatus}'"),

            ("errores",
             m.RequestCount >= _opciones.ErrorRateMinRequests &&
                 (double)m.ErrorCount / m.RequestCount * 100 >= _opciones.ErrorRateAlertPercent,
             normal,
             $"{m.ErrorCount} de {m.RequestCount} peticiones devolvieron 5xx"),

            // Los 4xx se registran y se pintan, pero NO avisan: con bots barriendo la IP,
            // un goteo de 404 y 401 es el estado normal de cualquier servidor público.
            // Una alerta que salta cada día deja de leerse. Un 429 sostenido es otra cosa:
            // no es que alguien probó una URL que no existe, es que el MISMO limitador se
            // sigue disparando minuto tras minuto — una señal mucho más rara y específica.
            ("abuso",
             m.RateLimitedCount >= _opciones.RateLimitAbuseThreshold,
             normal,
             $"{m.RateLimitedCount} peticiones bloqueadas por rate limiting en el último minuto (umbral {_opciones.RateLimitAbuseThreshold}, {normal} minutos seguidos)")
        };
    }

    private async Task EvaluarAvisosAsync(
        IServiceProvider sp, ServerMetric m, bool enviar, CancellationToken ct)
    {
        var settingManager = sp.GetRequiredService<ISettingManager>();

        await CargarEstadoSiHaceFaltaAsync(settingManager);

        var cambio = false;

        foreach (var (clave, fuera, minimo, mensaje) in Reglas(m))
        {
            // Racha de muestras consecutivas fuera de rango. Un solo valor bueno la
            // reinicia: así un pico aislado nunca llega a acumular.
            var racha = fuera ? _rachas.GetValueOrDefault(clave) + 1 : 0;
            _rachas[clave] = racha;

            var deberiaEstarActiva = racha >= minimo;
            var estaActiva = _avisosActivos.GetValueOrDefault(clave);

            // Solo en los CAMBIOS de estado. Avisar cada minuto mientras el disco siga
            // lleno convierte la alerta en ruido y deja de leerse a los dos días.
            if (deberiaEstarActiva == estaActiva)
            {
                continue;
            }

            _avisosActivos[clave] = deberiaEstarActiva;
            cambio = true;

            if (!enviar)
            {
                continue;
            }

            var titulo = deberiaEstarActiva ? "🔴 Alerta" : "✅ Resuelto";
            await EnviarAsync(sp, $"{titulo}: {clave}", mensaje, ct);
        }

        if (cambio)
        {
            await GuardarEstadoAsync(settingManager);
        }
    }

    /// <summary>
    /// El worker vive en el proceso de la API, así que cada despliegue le borra la memoria.
    /// Sin persistir esto, un disco al 90% generaba una alerta nueva en CADA despliegue.
    /// </summary>
    private async Task CargarEstadoSiHaceFaltaAsync(ISettingManager settingManager)
    {
        if (_estadoCargado)
        {
            return;
        }

        _estadoCargado = true;

        try
        {
            var json = await settingManager.GetOrNullGlobalAsync(MonitoringSettingNames.AlertState);
            if (!json.IsNullOrWhiteSpace())
            {
                _avisosActivos = JsonSerializer.Deserialize<Dictionary<string, bool>>(json!)
                    ?? new Dictionary<string, bool>();
            }
        }
        catch (Exception ex)
        {
            // Un estado corrupto no puede impedir la recolección: se empieza de cero y,
            // como mucho, se repite un aviso.
            Logger.LogWarning(ex, "No se pudo leer el estado de alertas; se empieza de cero");
            _avisosActivos = new Dictionary<string, bool>();
        }
    }

    private async Task GuardarEstadoAsync(ISettingManager settingManager)
    {
        try
        {
            // Solo las activas: guardar los 'false' haría crecer el JSON con ruido.
            var activas = _avisosActivos.Where(p => p.Value).ToDictionary(p => p.Key, p => p.Value);

            await settingManager.SetGlobalAsync(
                MonitoringSettingNames.AlertState, JsonSerializer.Serialize(activas));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "No se pudo guardar el estado de alertas");
        }
    }

    private async Task EnviarAsync(
        IServiceProvider sp, string titulo, string cuerpo, CancellationToken ct)
    {
        try
        {
            // UNA llamada para los DOS canales: la campana de la aplicación y Telegram.
            // Antes esto encolaba Telegram a mano; al añadir los avisos en la aplicación,
            // repetir aquí las dos escrituras habría dejado que un día se añadiera un aviso
            // nuevo en un canal y no en el otro, sin que nada fallara. Ver Avisador.
            var avisador = sp.GetRequiredService<IAvisador>();
            await avisador.AvisarAsync(
                titulo,
                cuerpo,
                // Que salte una regla de monitoreo es siempre degradación, no información.
                NotificationSeverity.Warning,
                AuxiliarContablePermissions.Metrics.Default,
                url: "/metrics",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Avisar no puede tumbar la recolección: un recolector que muere porque no pudo
            // avisar deja de recolectar, que es peor que el aviso perdido.
            Logger.LogWarning(ex, "No se pudo avisar de monitoreo: {Titulo}", titulo);
        }
    }
}
