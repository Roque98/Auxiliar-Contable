using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Backups;
using IgasConsole.Jobs;
using IgasConsole.Monitoring;
using IgasConsole.Notifications;
using IgasConsole.Permissions;
using IgasConsole.Security;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization.Permissions;

namespace IgasConsole.SystemStatus;

/// <summary>
/// El panel de «cómo va todo». Junta en una llamada lo que estaba repartido entre cinco
/// pantallas y dos temporizadores de systemd.
///
/// <para>Devuelve **recuentos, no detalle**. Es lo que permite que baste un solo permiso:
/// saber que hay tres trabajos abandonados no es lo mismo que poder leer sus argumentos,
/// y para eso último sigue haciendo falta <c>IgasConsole.Jobs</c>.</para>
/// </summary>
[Authorize(IgasConsolePermissions.SystemStatus.Default)]
public class SystemStatusAppService : IgasConsoleAppService, ISystemStatusAppService
{
    private readonly IBackupStatusReader _copias;
    private readonly IServerMetricRepository _metricas;
    private readonly IJobMaintenanceRepository _trabajos;
    private readonly IBlockedIpRepository _ips;
    private readonly INotificacionRepository _avisos;
    private readonly IPermissionChecker _permisos;

    public SystemStatusAppService(
        IBackupStatusReader copias,
        IServerMetricRepository metricas,
        IJobMaintenanceRepository trabajos,
        IBlockedIpRepository ips,
        INotificacionRepository avisos,
        IPermissionChecker permisos)
    {
        _copias = copias;
        _metricas = metricas;
        _trabajos = trabajos;
        _ips = ips;
        _avisos = avisos;
        _permisos = permisos;
    }

    public async Task<SystemStatusDto> GetAsync()
    {
        var ahora = DateTime.UtcNow;

        var ultima = await _metricas.GetLatestAsync();
        var estadosTrabajos = await _trabajos.GetCountsByStateAsync();
        var bloqueadas = await _ips.GetVigentesAsync(ahora);

        var dto = new SystemStatusDto
        {
            // Sale de la última muestra del recolector, no de sondear otra vez: ya lo hizo
            // hace un minuto y repetirlo aquí cargaría la base en cada carga del panel.
            Health = ultima?.HealthStatus ?? MonitoringConsts.HealthUnknown,
            UptimeSeconds = ultima?.UptimeSeconds ?? 0,
            AbandonedJobs = estadosTrabajos.GetValueOrDefault(JobState.Abandonado),
            PendingJobs = estadosTrabajos.GetValueOrDefault(JobState.Esperando)
                          + estadosTrabajos.GetValueOrDefault(JobState.Reintentando),
            ActiveBlockedIps = bloqueadas.Count,
            Backup = MapearCopia(await _copias.LeerCopiaAsync(), ahora),
            RestoreCheck = MapearVerificacion(await _copias.LeerVerificacionAsync(), ahora)
        };

        // Los avisos SÍ son de cada uno: se cuentan los que ve quien pregunta, no todos.
        var candidatos = await _avisos.GetTargetPermissionsAsync();
        var mios = new List<string>();
        foreach (var permiso in candidatos)
        {
            if (await _permisos.IsGrantedAsync(permiso))
            {
                mios.Add(permiso);
            }
        }

        if (CurrentUser.Id.HasValue)
        {
            dto.UnreadNotifications = await _avisos.CountUnreadAsync(CurrentUser.Id.Value, mios);
        }

        return dto;
    }

    private static BackupStatusDto MapearCopia(BackupStatus? copia, DateTime ahora)
    {
        if (copia == null)
        {
            return new BackupStatusDto
            {
                State = "SinInformacion",
                Summary = "No hay ninguna copia registrada."
            };
        }

        var edad = copia.EdadHoras(ahora);

        // El orden va de más grave a menos: una copia que falla importa más que una que
        // no sale del servidor, y esa más que una antigua.
        var estado = !copia.Ok ? "Fallando"
            : copia.EstaCaducada(ahora) ? "Caducada"
            : copia.Remote != "ok" ? "SoloLocal"
            : "Ok";

        var resumen = estado switch
        {
            "Fallando" => $"El último intento falló en la etapa «{copia.Stage}».",
            "Caducada" => $"La última correcta es de hace {edad:F0} h; debería haber una cada día.",
            "SoloLocal" => "Se hace, pero no sale del servidor: no protege de perder la máquina.",
            _ => $"Hace {edad:F0} h · {copia.Tables} tablas · {copia.RemoteSnapshots} copias fuera."
        };

        return new BackupStatusDto
        {
            State = estado,
            LastRunUtc = copia.LastRunUtc,
            AgeHours = edad,
            DumpBytes = copia.DumpBytes,
            Tables = copia.Tables,
            RemoteSnapshots = copia.RemoteSnapshots,
            Summary = resumen
        };
    }

    private static RestoreCheckDto MapearVerificacion(RestoreCheckStatus? v, DateTime ahora)
    {
        if (v == null)
        {
            return new RestoreCheckDto
            {
                State = "SinInformacion",
                Summary = "Nunca se ha comprobado que la copia se pueda restaurar."
            };
        }

        var edad = v.EdadDias(ahora);
        var estado = !v.Ok ? "Fallando" : v.EstaCaducada(ahora) ? "Caducada" : "Ok";

        var resumen = estado switch
        {
            "Fallando" => $"La última restauración de prueba falló: {v.Message}",
            "Caducada" => $"La última fue hace {edad:F0} días; debería hacerse cada mes.",
            _ => $"Restaurada hace {edad:F0} días · {v.TablesRestored} tablas."
        };

        return new RestoreCheckDto
        {
            State = estado,
            LastRunUtc = v.LastRunUtc,
            AgeDays = edad,
            TablesRestored = v.TablesRestored,
            Summary = resumen
        };
    }
}
