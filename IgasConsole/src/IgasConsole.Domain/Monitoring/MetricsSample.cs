namespace IgasConsole.Monitoring;

/// <summary>Lo que la sonda de sistema consigue leer. Todo nullable: fuera de Linux no hay /proc.</summary>
public class SystemSample
{
    public double? CpuPercent { get; set; }
    public int? MemoryUsedMb { get; set; }
    public int? MemoryTotalMb { get; set; }
    public int? SwapUsedMb { get; set; }
    public int? SwapTotalMb { get; set; }
    public double? DiskUsedPercent { get; set; }
}

/// <summary>Recuento de peticiones del intervalo que se acaba de cerrar.</summary>
public class RequestSample
{
    public int RequestCount { get; set; }
    public int ErrorCount { get; set; }
    public int ClientErrorCount { get; set; }
    public double? AvgResponseMs { get; set; }
    public double? MaxResponseMs { get; set; }
}
