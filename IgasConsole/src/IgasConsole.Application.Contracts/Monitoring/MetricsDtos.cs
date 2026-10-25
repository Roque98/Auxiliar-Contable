using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IgasConsole.Monitoring;

public class GetMetricsInput
{
    /// <summary>Ventana a mostrar, en horas. 1 = última hora, 24 = un día, 168 = una semana.</summary>
    [Range(1, 168)]
    public int Hours { get; set; } = 24;
}

public class MetricPointDto
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
    public long Restarts { get; set; }
    public long RateLimitedCount { get; set; }
}

public class MetricsSeriesDto
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }

    /// <summary>Minutos que agrupa cada punto. Lo decide el servidor según la ventana.</summary>
    public int BucketMinutes { get; set; }

    public List<MetricPointDto> Points { get; set; } = new();

    /// <summary>Última muestra tomada, para las tarjetas de estado actual. Null si no hay ninguna.</summary>
    public MetricPointDto? Latest { get; set; }

    /// <summary>
    /// Segundos desde la última muestra. Es la métrica MÁS importante de todas: si el
    /// recolector muere, los gráficos se quedan quietos con datos viejos y la página
    /// parece perfectamente sana. Un monitoreo que falla en silencio engaña.
    /// </summary>
    public double? SecondsSinceLastSample { get; set; }

    /// <summary>Cada cuántos segundos debería llegar una muestra, para que el cliente
    /// sepa cuándo preocuparse sin llevar el número a fuego.</summary>
    public int SampleIntervalSeconds { get; set; }
}
