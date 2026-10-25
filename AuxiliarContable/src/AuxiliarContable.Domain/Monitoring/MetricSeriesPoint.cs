using System;

namespace AuxiliarContable.Monitoring;

/// <summary>
/// Punto de una serie temporal ya agregada por la base. Read model: no es una entidad,
/// no se mapea en el DbContext y solo lo materializa <c>SqlQueryRaw</c>.
/// </summary>
public class MetricSeriesPoint
{
    public DateTime Bucket { get; set; }
    public double? CpuPercent { get; set; }
    public double? MemoryPercent { get; set; }
    public double? DiskUsedPercent { get; set; }
    public long RequestCount { get; set; }
    public long ErrorCount { get; set; }
    public double? AvgResponseMs { get; set; }
    public double? MaxResponseMs { get; set; }
    public long UnhealthySamples { get; set; }
    public double? SwapPercent { get; set; }
    public double? DbSizeMb { get; set; }
    public double? DbConnections { get; set; }
    public long ClientErrorCount { get; set; }

    /// <summary>Cuántos reinicios del proceso cayeron en este cubo.</summary>
    public long Restarts { get; set; }

    public long RateLimitedCount { get; set; }
}

/// <summary>Estadísticas puntuales de la base. Read model de GetDatabaseStats.sql.</summary>
public class DatabaseStats
{
    public int DbSizeMb { get; set; }
    public int DbConnections { get; set; }
}
