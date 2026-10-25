using System;
using System.Threading.Tasks;
using AuxiliarContable.Backups;
using AuxiliarContable.Notifications;
using AuxiliarContable.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Avisa cuando la copia de seguridad deja de hacerse, falla, o no sale del servidor.
///
/// <para>Es la pieza que faltaba. Hasta ahora la copia corría a las 03:30, y si fallaba lo
/// único que ocurría era que systemd anotaba un servicio en estado <c>failed</c> — algo que
/// nadie mira. Podía llevar semanas rota y solo se habría descubierto el día de intentar
/// restaurar, que es el peor día posible para descubrirlo.</para>
///
/// <para>Un caso concreto y nada teórico: el token de R2 está limitado a la IPv4 del VPS.
/// Si el proveedor la cambia, la subida empieza a fallar con un 403 y todo lo demás sigue
/// funcionando con normalidad.</para>
/// </summary>
public class BackupWatchWorker : AsyncPeriodicBackgroundWorkerBase
{
    public BackupWatchWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromHours(BackupConsts.WatchIntervalHours).TotalMilliseconds;
        // Recién arrancado es cuando interesa saber en qué estado quedó todo. El freno por
        // título impide que los reinicios repitan el aviso.
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var lector = workerContext.ServiceProvider.GetRequiredService<IBackupStatusReader>();

        var copia = await lector.LeerCopiaAsync();
        var verificacion = await lector.LeerVerificacionAsync();

        var ahora = DateTime.UtcNow;

        var (titulo, cuerpo, severidad) = BackupAlertRules.Evaluar(copia, verificacion, ahora);

        if (titulo == null)
        {
            return;
        }

        // Un worker no viene envuelto en unidad de trabajo. Ver CLAUDE.md §6.0.
        var unidades = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var unidad = unidades.Begin(requiresNew: true);

        var avisos = workerContext.ServiceProvider.GetRequiredService<INotificacionRepository>();

        if (await avisos.ExistsRecentAsync(
                titulo, null, ahora.AddHours(-BackupConsts.SilenceHours)))
        {
            return;
        }

        var avisador = workerContext.ServiceProvider.GetRequiredService<IAvisador>();
        await avisador.AvisarAsync(
            titulo, cuerpo!, severidad, AuxiliarContablePermissions.SystemStatus.Default, "/system-status");

        await unidad.CompleteAsync();

        Logger.LogWarning("Aviso de copias: {Titulo}", titulo);
    }

}
