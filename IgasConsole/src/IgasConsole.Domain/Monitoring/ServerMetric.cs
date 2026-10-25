using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace IgasConsole.Monitoring;

/// <summary>
/// Una muestra de monitoreo del servidor. A diferencia de <c>Product</c>, esta entidad
/// NO es multi-tenant ni tiene soft delete, y es deliberado:
/// <list type="bullet">
/// <item>Multi-tenant no: mide el proceso y la máquina, no los datos de ningún tenant.
/// Si implementara <c>IMultiTenant</c>, el filtro global escondería las muestras que
/// tomó el worker (que corre sin tenant) al mirarlas desde un tenant.</item>
/// <item>Soft delete no: la purga por retención tiene que BORRAR de verdad. Con
/// <c>ISoftDelete</c> las filas se quedarían para siempre ocupando disco, marcadas.</item>
/// </list>
/// Los campos de sistema son nullable porque en Windows (máquina de desarrollo) no hay
/// <c>/proc</c> y la sonda no puede rellenarlos.
/// </summary>
public class ServerMetric : Entity<Guid>
{
    /// <summary>Instante de la muestra. SIEMPRE UTC: EF mapea a timestamptz y Npgsql
    /// rechaza en tiempo de ejecución cualquier DateTime que no sea Kind.Utc.</summary>
    public DateTime Timestamp { get; protected set; }

    // --- Sistema (null fuera de Linux) ---
    public double? CpuPercent { get; protected set; }
    public int? MemoryUsedMb { get; protected set; }
    public int? MemoryTotalMb { get; protected set; }
    public int? SwapUsedMb { get; protected set; }

    /// <summary>Sin el total no se puede calcular el porcentaje, y un "512 MB de swap en
    /// uso" no dice nada por sí solo: depende de si hay 1 GB o 16.</summary>
    public int? SwapTotalMb { get; protected set; }

    public double? DiskUsedPercent { get; protected set; }

    /// <summary>Tamaño de la base principal. Crece despacio y en silencio hasta que un
    /// día llena el disco; verlo en una serie da meses de aviso.</summary>
    public int? DbSizeMb { get; protected set; }

    /// <summary>Conexiones abiertas a PostgreSQL. Si se acerca al máximo del servidor,
    /// la API empieza a fallar con errores que no se parecen en nada a la causa.</summary>
    public int? DbConnections { get; protected set; }

    // --- Aplicación (del intervalo que acaba de cerrarse) ---
    public int RequestCount { get; protected set; }

    /// <summary>Respuestas 5xx: el servidor falló.</summary>
    public int ErrorCount { get; protected set; }

    /// <summary>Respuestas 4xx. No son fallos del servidor, pero una avalancha de 404 o
    /// 401 suele ser la primera señal de que algo se rompió — un despliegue que dejó
    /// rutas colgando, tokens que caducan mal, o alguien probando URLs.</summary>
    public int ClientErrorCount { get; protected set; }
    public double? AvgResponseMs { get; protected set; }
    public double? MaxResponseMs { get; protected set; }

    /// <summary>Peticiones rechazadas por rate limiting en el intervalo. A diferencia de
    /// ClientErrorCount (que incluye el goteo normal de bots probando URLs), esto es
    /// tráfico que el propio limitador ya identificó como abuso.</summary>
    public int RateLimitedCount { get; protected set; }

    public string HealthStatus { get; protected set; }

    /// <summary>Segundos que lleva vivo el proceso. Un valor que baja de golpe delata un
    /// reinicio: la señal más importante que se puede perder si solo se miran promedios.</summary>
    public int? UptimeSeconds { get; protected set; }

    protected ServerMetric()
    {
        HealthStatus = MonitoringConsts.HealthUnknown;
    }

    public ServerMetric(Guid id, DateTime timestampUtc, string healthStatus)
        : base(id)
    {
        if (timestampUtc.Kind != DateTimeKind.Utc)
        {
            throw new AbpException(
                "ServerMetric.Timestamp debe ser UTC: la columna es timestamptz y Npgsql " +
                "lanza en tiempo de ejecución si el Kind no es Utc.");
        }

        Timestamp = timestampUtc;
        HealthStatus = Check.NotNullOrWhiteSpace(
            healthStatus, nameof(healthStatus), MonitoringConsts.MaxHealthStatusLength);
    }

    public ServerMetric SetSystem(double? cpuPercent, int? memoryUsedMb, int? memoryTotalMb,
        int? swapUsedMb, int? swapTotalMb, double? diskUsedPercent)
    {
        CpuPercent = cpuPercent;
        MemoryUsedMb = memoryUsedMb;
        MemoryTotalMb = memoryTotalMb;
        SwapUsedMb = swapUsedMb;
        SwapTotalMb = swapTotalMb;
        DiskUsedPercent = diskUsedPercent;
        return this;
    }

    public ServerMetric SetDatabase(int? dbSizeMb, int? dbConnections)
    {
        DbSizeMb = dbSizeMb;
        DbConnections = dbConnections;
        return this;
    }

    public ServerMetric SetProcess(int? uptimeSeconds)
    {
        UptimeSeconds = uptimeSeconds;
        return this;
    }

    public ServerMetric SetApplication(int requestCount, int errorCount, int clientErrorCount,
        double? avgResponseMs, double? maxResponseMs)
    {
        RequestCount = requestCount;
        ErrorCount = errorCount;
        ClientErrorCount = clientErrorCount;
        AvgResponseMs = avgResponseMs;
        MaxResponseMs = maxResponseMs;
        return this;
    }

    public ServerMetric SetRateLimiting(int rateLimitedCount)
    {
        RateLimitedCount = rateLimitedCount;
        return this;
    }
}
