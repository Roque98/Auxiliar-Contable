using System;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Cada 5 minutos avanza las solicitudes de Descarga Masiva en proceso (cada una se pregunta cada 15 minutos) y baja los
/// paquetes listos. Una vez al día, desde las 6:00 (hora de México) y a partir del día 3, pide sola la descarga del mes
/// anterior de los clientes que no mandaron nada (<see cref="DescargadorMasivo"/>).
/// </summary>
public class DescargaMasivaWorker : AsyncPeriodicBackgroundWorkerBase
{
    private DateOnly? _ultimoCierre;

    public DescargaMasivaWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var descargador = workerContext.ServiceProvider.GetRequiredService<DescargadorMasivo>();
        if (!descargador.EstaConfigurado)
        {
            return;
        }

        var log = workerContext.ServiceProvider.GetRequiredService<ILogger<DescargaMasivaWorker>>();
        var hoy = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ActualizadorDeImpuestos.ZonaDeMexico);
        var dia = DateOnly.FromDateTime(hoy);
        if (hoy.Hour >= 6 && _ultimoCierre != dia)
        {
            _ultimoCierre = dia;
            var pedidos = await descargador.PedirCierreDeMesAsync(hoy);
            if (pedidos > 0)
            {
                log.LogInformation("Descarga masiva del cierre: se pidió la de {Clientes} clientes.", pedidos);
            }
        }

        var v = await descargador.AvanzarAsync();
        if (v.Revisadas > 0)
        {
            log.LogInformation("Descarga masiva: {Revisadas} revisadas, {Terminadas} terminadas, {Nuevas} facturas nuevas, {Fallaron} fallaron.",
                v.Revisadas, v.Terminadas, v.Nuevas, v.Fallaron);
        }
    }
}
