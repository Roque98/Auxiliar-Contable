using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AuxiliarContable.Nomina;

/// <summary>Una vez al día, los avisos de servicios especializados (<see cref="RecordatorioRepse"/>). Corre al arrancar; el
/// freno por título impide repetir.</summary>
public class RecordatorioRepseWorker : AsyncPeriodicBackgroundWorkerBase
{
    public RecordatorioRepseWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromHours(24).TotalMilliseconds;
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        using var unidad = sp.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var n = await sp.GetRequiredService<RecordatorioRepse>().AvisarAsync(sp.GetRequiredService<IClock>().Now, DateTime.UtcNow);
        await unidad.CompleteAsync();
        if (n > 0)
        {
            Logger.LogInformation("Avisos de servicios especializados (REPSE): {Avisos}", n);
        }
    }
}
