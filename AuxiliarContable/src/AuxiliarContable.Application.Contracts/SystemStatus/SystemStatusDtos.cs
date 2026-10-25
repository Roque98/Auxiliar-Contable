using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.SystemStatus;

public class BackupStatusDto
{
    /// <summary>Ok | Fallando | Caducada | SoloLocal | SinInformacion.</summary>
    public string State { get; set; } = string.Empty;

    public DateTime? LastRunUtc { get; set; }

    public double? AgeHours { get; set; }

    public long DumpBytes { get; set; }

    public int Tables { get; set; }

    /// <summary>Cuántos snapshots hay en el destino remoto. 0 si no sale del servidor.</summary>
    public int RemoteSnapshots { get; set; }

    /// <summary>Frase lista para enseñar. La compone el servidor para no repetir la regla.</summary>
    public string Summary { get; set; } = string.Empty;
}

public class RestoreCheckDto
{
    /// <summary>Ok | Fallando | Caducada | SinInformacion.</summary>
    public string State { get; set; } = string.Empty;

    public DateTime? LastRunUtc { get; set; }

    public double? AgeDays { get; set; }

    public int TablesRestored { get; set; }

    public string Summary { get; set; } = string.Empty;
}

public class SystemStatusDto
{
    /// <summary>Healthy | Degraded | Unhealthy — el mismo de <c>/health-status</c>.</summary>
    public string Health { get; set; } = string.Empty;

    public BackupStatusDto Backup { get; set; } = new();

    public RestoreCheckDto RestoreCheck { get; set; } = new();

    public int AbandonedJobs { get; set; }

    public int PendingJobs { get; set; }

    public int ActiveBlockedIps { get; set; }

    public int UnreadNotifications { get; set; }

    /// <summary>Segundos que lleva el proceso en marcha. Un valor bajo delata un reinicio.</summary>
    public long UptimeSeconds { get; set; }
}

/// <summary>
/// Un solo sitio al que mirar. Junta lo que hoy está repartido entre cinco pantallas y
/// dos temporizadores de systemd.
///
/// <para>Devuelve **solo recuentos**: el detalle de cada cosa sigue detrás de su propio
/// permiso. Saber que hay tres trabajos abandonados no es lo mismo que poder leer sus
/// argumentos.</para>
/// </summary>
public interface ISystemStatusAppService : IApplicationService
{
    Task<SystemStatusDto> GetAsync();
}
