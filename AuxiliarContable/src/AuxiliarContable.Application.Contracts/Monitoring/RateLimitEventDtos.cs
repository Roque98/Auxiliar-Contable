using System;
using System.ComponentModel.DataAnnotations;

namespace AuxiliarContable.Monitoring;

public class GetRateLimitEventsInput
{
    /// <summary>Ventana a mostrar, en horas.</summary>
    [Range(1, 168)]
    public int Hours { get; set; } = 24;

    [Range(1, 500)]
    public int MaxResults { get; set; } = 200;
}

public class RateLimitEventDto
{
    public DateTime WindowStartUtc { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int RejectionCount { get; set; }
    public DateTime FirstRejectedUtc { get; set; }
    public DateTime LastRejectedUtc { get; set; }
}
