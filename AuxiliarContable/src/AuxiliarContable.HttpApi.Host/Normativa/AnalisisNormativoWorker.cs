using System;
using System.Threading.Tasks;
using AuxiliarContable.Normativa;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.Normativa;

/// <summary>
/// Cada 30 s hace el siguiente análisis normativo pedido (uno a la vez: cada uno es una llamada larga a Claude en el
/// servidor del motor).
/// </summary>
public class AnalisisNormativoWorker : AsyncPeriodicBackgroundWorkerBase
{
    public AnalisisNormativoWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var hechos = await workerContext.ServiceProvider.GetRequiredService<AnalizadorNormativo>().ProcesarAsync();
        if (hechos > 0)
        {
            workerContext.ServiceProvider.GetRequiredService<ILogger<AnalisisNormativoWorker>>()
                .LogInformation("Análisis normativo: {Hechos} terminado(s).", hechos);
        }
    }
}
