using System;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Cada 20 segundos recalcula los impuestos del mes que quedaron «por actualizar» porque entró, cambió o se borró una
/// factura (ver <see cref="ActualizadorDeImpuestos"/>): así el cálculo del auxiliar está al día casi en tiempo real.
/// </summary>
public class ActualizadorDeImpuestosWorker : AsyncPeriodicBackgroundWorkerBase
{
    public ActualizadorDeImpuestosWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(20).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext) =>
        await workerContext.ServiceProvider.GetRequiredService<ActualizadorDeImpuestos>().ActualizarAsync();
}
