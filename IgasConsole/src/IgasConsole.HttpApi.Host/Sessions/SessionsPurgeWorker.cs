using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Identity;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Borra las sesiones que llevan mucho sin actividad.
///
/// <para>Hace falta porque casi nadie cierra sesión de forma explícita: cada login deja una
/// fila y, sin purga, la lista acabaría llena de sesiones muertas de hace meses. Una lista
/// que no se puede leer es tan inútil como no tenerla — y aquí es peor, porque la gracia de
/// esta pantalla es reconocer lo que NO deberías ver.</para>
/// </summary>
public class SessionsPurgeWorker : AsyncPeriodicBackgroundWorkerBase
{
    public SessionsPurgeWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan
            .FromHours(SessionConsts.PurgeIntervalHours).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var uowManager = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true);

        var sesiones = workerContext.ServiceProvider.GetRequiredService<IIdentitySessionRepository>();

        var corte = DateTime.UtcNow.AddDays(-SessionConsts.InactivityRetentionDays);

        var todas = await sesiones.GetListAsync();

        // Se compara contra LastAccessed y, si nunca se usó, contra el momento de entrar:
        // una sesión abierta y jamás vuelta a tocar también envejece.
        var caducadas = todas
            .Where(s => (s.LastAccessed ?? s.SignedIn) < corte)
            .ToList();

        foreach (var sesion in caducadas)
        {
            await sesiones.DeleteAsync(sesion);
        }

        await uow.CompleteAsync();

        if (caducadas.Count > 0)
        {
            Logger.LogInformation(
                "Purgadas {Cuantas} sesiones sin actividad en {Dias} días",
                caducadas.Count, SessionConsts.InactivityRetentionDays);
        }
    }
}
