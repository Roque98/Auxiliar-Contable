using System;
using System.Threading.Tasks;
using AuxiliarContable.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Borra los avisos viejos y, con ellos, sus marcas de lectura.
///
/// <para>Sin esto la campana acumularía meses de avisos ya atendidos. El problema no es el
/// tamaño de la tabla —son filas diminutas— sino que una lista larga deja de leerse, y
/// entonces el aviso de hoy vale lo mismo que no tenerlo.</para>
/// </summary>
public class NotificationsPurgeWorker : AsyncPeriodicBackgroundWorkerBase
{
    public NotificationsPurgeWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan
            .FromHours(NotificationConsts.PurgeIntervalHours).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var unidades = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var unidad = unidades.Begin(requiresNew: true);

        var avisos = workerContext.ServiceProvider.GetRequiredService<INotificacionRepository>();

        var corte = DateTime.UtcNow.AddDays(-NotificationConsts.RetentionDays);

        var borrados = await avisos.PurgeOlderThanAsync(corte);

        await unidad.CompleteAsync();

        if (borrados > 0)
        {
            Logger.LogInformation(
                "Purgados {Cuantos} avisos con más de {Dias} días",
                borrados, NotificationConsts.RetentionDays);
        }
    }
}
