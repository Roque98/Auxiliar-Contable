using System;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Cada minuto interpreta con Claude las operaciones que se pidieron desde la mesa (ver
/// <see cref="InterpretadorDeOperaciones"/>). De dos en dos: cada una tarda.
/// </summary>
public class InterpretacionesWorker : AsyncPeriodicBackgroundWorkerBase
{
    public InterpretacionesWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var hechas = await workerContext.ServiceProvider.GetRequiredService<InterpretadorDeOperaciones>().ProcesarAsync();
        if (hechas > 0)
        {
            workerContext.ServiceProvider.GetRequiredService<ILogger<InterpretacionesWorker>>()
                .LogInformation("Interpretaciones: {Operaciones} operaciones interpretadas con Claude.", hechas);
        }
    }
}
