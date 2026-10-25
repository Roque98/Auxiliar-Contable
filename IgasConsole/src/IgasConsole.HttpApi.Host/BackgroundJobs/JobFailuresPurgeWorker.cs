using System;
using System.Threading.Tasks;
using IgasConsole.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace IgasConsole.BackgroundJobs;

/// <summary>
/// Borra el historial de fallos que ya nadie va a mirar.
///
/// <para>Hace falta porque nada más lo borra: la fila del trabajo desaparece al completarse
/// —o al borrarla desde el panel— y su historial sobrevive a propósito, para que «esto ya
/// nos pasó el mes pasado» siga siendo contestable. Sin purga, un trabajo que falla en
/// bucle durante días dejaría filas para siempre.</para>
/// </summary>
public class JobFailuresPurgeWorker : AsyncPeriodicBackgroundWorkerBase
{
    public JobFailuresPurgeWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan
            .FromHours(JobConsts.PurgeIntervalHours).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var unidades = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var unidad = unidades.Begin(requiresNew: true);

        var fallos = workerContext.ServiceProvider.GetRequiredService<IJobFailureRepository>();

        var corte = DateTime.UtcNow.AddDays(-JobConsts.FailureRetentionDays);

        var borradas = await fallos.PurgeOlderThanAsync(corte);

        await unidad.CompleteAsync();

        if (borradas > 0)
        {
            Logger.LogInformation(
                "Purgados {Cuantos} fallos de trabajos con más de {Dias} días",
                borradas, JobConsts.FailureRetentionDays);
        }
    }
}
