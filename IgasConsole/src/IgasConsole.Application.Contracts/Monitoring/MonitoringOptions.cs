namespace IgasConsole.Monitoring;

/// <summary>
/// Umbrales de aviso. Se configuran desde appsettings o variables de entorno
/// (sección <c>Monitoring</c>) para poder ajustarlos sin recompilar.
/// </summary>
public class MonitoringOptions
{
    /// <summary>Recolectar métricas. Se apaga en tests y en el DbMigrator.</summary>
    public bool IsEnabled { get; set; } = true;

    public double DiskAlertPercent { get; set; } = 85;

    /// <summary>CPU media sostenida durante un intervalo completo.</summary>
    public double CpuAlertPercent { get; set; } = 90;

    public double MemoryAlertPercent { get; set; } = 90;

    /// <summary>Porcentaje de swap en uso. Que un servidor con memoria de sobra empiece a
    /// tirar de swap suele avisar de un problema antes de que la memoria llegue al 90%.</summary>
    public double SwapAlertPercent { get; set; } = 25;

    /// <summary>Conexiones abiertas a PostgreSQL. Acercarse al límite del servidor produce
    /// errores que no se parecen en nada a su causa.</summary>
    public int DbConnectionsAlert { get; set; } = 80;

    /// <summary>Porcentaje de peticiones con error 5xx que dispara el aviso.</summary>
    public double ErrorRateAlertPercent { get; set; } = 10;

    /// <summary>Mínimo de peticiones en el intervalo para que la tasa de error signifique algo.</summary>
    public int ErrorRateMinRequests { get; set; } = 20;

    /// <summary>Peticiones bloqueadas por rate limiting en un minuto que se consideran
    /// abuso sostenido (no un pico aislado) — igual que las demás reglas, hace falta
    /// MuestrasConsecutivasParaAvisar minutos seguidos por encima de esto para avisar.</summary>
    public int RateLimitAbuseThreshold { get; set; } = 30;

    // --- Telegram (los valores reales van en /etc/zia-console/env, nunca en git) ---
    public string? TelegramBotToken { get; set; }
    public string? TelegramChatId { get; set; }
}
