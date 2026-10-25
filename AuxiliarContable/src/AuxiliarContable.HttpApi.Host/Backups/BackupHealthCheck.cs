using System;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Backups;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Salud de las copias de seguridad.
///
/// <para>⚠️ NUNCA devuelve <c>Unhealthy</c>, y es a propósito. El despliegue comprueba
/// <c>/health-status</c> y aborta si no responde 200; ASP.NET devuelve 503 para
/// <c>Unhealthy</c> y 200 para <c>Degraded</c>. Marcar una copia fallida como
/// <c>Unhealthy</c> **bloquearía los despliegues** —incluido el que traería el arreglo— y
/// haría que la aplicación pareciera caída cuando está sirviendo perfectamente.</para>
///
/// <para>Que la copia falle es urgente, pero la urgencia la llevan los avisos
/// (<see cref="BackupWatchWorker"/>), no el semáforo de salud.</para>
/// </summary>
public class BackupHealthCheck : IHealthCheck
{
    private readonly IBackupStatusReader _lector;

    public BackupHealthCheck(IBackupStatusReader lector)
    {
        _lector = lector;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var copia = await _lector.LeerCopiaAsync(cancellationToken);

        if (copia == null)
        {
            // Sin información: normal en desarrollo, sospechoso en producción. Degradado
            // lo deja visible sin gritar.
            return HealthCheckResult.Degraded("Sin información de copias de seguridad.");
        }

        var ahora = DateTime.UtcNow;

        if (!copia.Ok)
        {
            return HealthCheckResult.Degraded(
                $"La última copia falló en la etapa «{copia.Stage}» " +
                $"({copia.LastRunUtc:yyyy-MM-dd HH:mm} UTC).");
        }

        if (copia.EstaCaducada(ahora))
        {
            var edad = copia.EdadHoras(ahora);
            return HealthCheckResult.Degraded(
                $"La última copia correcta es de hace {edad:F0} h " +
                $"(máximo {BackupConsts.MaxBackupAgeHours} h).");
        }

        if (copia.Remote != "ok")
        {
            // Se hizo la copia, pero no salió del servidor. Es media protección: sirve
            // contra un borrado accidental, no contra perder la máquina.
            return HealthCheckResult.Degraded(
                $"La copia se hizo pero no salió del servidor (remoto: {copia.Remote}).");
        }

        var verificacion = await _lector.LeerVerificacionAsync(cancellationToken);
        if (verificacion is { Ok: false })
        {
            return HealthCheckResult.Degraded(
                $"La última verificación por restauración falló: {verificacion.Message}");
        }

        return HealthCheckResult.Healthy(
            $"Copia de hace {copia.EdadHoras(ahora):F0} h, {copia.RemoteSnapshots} snapshots fuera.");
    }
}
