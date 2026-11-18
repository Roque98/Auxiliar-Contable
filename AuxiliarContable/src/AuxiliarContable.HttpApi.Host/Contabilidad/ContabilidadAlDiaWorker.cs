using System;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Cada minuto pone al día la contabilidad de los meses que cambiaron y llevan 10 minutos sin cambios (ver
/// <see cref="ContabilidadAlDia"/>): conciliar el banco, amarrar lo seguro y rehacer las pólizas. No usa Claude.
/// </summary>
public class ContabilidadAlDiaWorker : AsyncPeriodicBackgroundWorkerBase
{
    public ContabilidadAlDiaWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var alDia = workerContext.ServiceProvider.GetRequiredService<ContabilidadAlDia>();
        var hechos = await alDia.ActualizarAsync();
        if (hechos > 0)
        {
            workerContext.ServiceProvider.GetRequiredService<ILogger<ContabilidadAlDiaWorker>>()
                .LogInformation("Contabilidad al día: {Meses} meses actualizados.", hechos);
        }
    }
}
