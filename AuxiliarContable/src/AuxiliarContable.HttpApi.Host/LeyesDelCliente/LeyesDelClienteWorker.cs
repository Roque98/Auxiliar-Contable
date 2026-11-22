using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.LeyesDelCliente;

/// <summary>
/// Cada 20 s: lee en el motor las leyes que subieron los clientes, analiza las que cambiaron de perfil o de ejercicio
/// (una a la vez: es un claude -p) y contesta preguntas sobre ellas.
/// </summary>
public class LeyesDelClienteWorker : AsyncPeriodicBackgroundWorkerBase
{
    public LeyesDelClienteWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(20).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var hechos = await workerContext.ServiceProvider.GetRequiredService<ProcesadorDeLeyesDelCliente>().ProcesarAsync();
        if (hechos > 0)
        {
            workerContext.ServiceProvider.GetRequiredService<ILogger<LeyesDelClienteWorker>>()
                .LogInformation("Mis leyes: {Hechos} paso(s) hechos.", hechos);
        }
    }
}
