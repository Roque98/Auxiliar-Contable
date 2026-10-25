using System;
using System.Threading.Tasks;
using AuxiliarContable.Security;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Relee la lista de IPs bloqueadas cada cierto tiempo.
///
/// <para>Al bloquear desde el panel la caché se refresca al instante, así que este worker
/// parece sobrar. No sobra por dos casos: los bloqueos que CADUCAN solos (nadie pulsa nada
/// cuando vence un plazo) y, sobre todo, el día que haya más de un servidor — cada proceso
/// tiene su propia copia en memoria, y el refresco directo solo alcanza al que atendió la
/// petición.</para>
///
/// <para>También hace la primera carga al arrancar: sin ella, tras cada despliegue la
/// aplicación arrancaría con la lista vacía y dejaría entrar a todo el mundo hasta el
/// primer ciclo.</para>
/// </summary>
public class BlockedIpsRefreshWorker : AsyncPeriodicBackgroundWorkerBase
{
    public BlockedIpsRefreshWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan
            .FromSeconds(SecurityConsts.BlockedIpsRefreshSeconds).TotalMilliseconds;

        // RunOnStart: la lista tiene que estar cargada desde la primera peticion, no un
        // ciclo despues.
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var cache = workerContext.ServiceProvider.GetRequiredService<IBlockedIpCache>();
        await cache.RefrescarAsync();
    }
}
