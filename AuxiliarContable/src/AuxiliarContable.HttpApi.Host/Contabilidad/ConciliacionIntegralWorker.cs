using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.Contabilidad;

/// <summary>Cada 10 s hace la siguiente vuelta del botón «Conciliar» que esté pedida (una a la vez).</summary>
public class ConciliacionIntegralWorker : AsyncPeriodicBackgroundWorkerBase
{
    public ConciliacionIntegralWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(10).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        await workerContext.ServiceProvider.GetRequiredService<ConciliadorIntegral>().ProcesarAsync();
    }
}
