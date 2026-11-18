using System;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Una vez cada noche, entre las 0:00 y las 5:00 (hora de México), pregunta al SAT el estatus de las facturas en proceso
/// de cancelación, de las nunca revisadas y de las del mes en curso y el anterior (<see cref="RevisorDeEstatusSat"/>).
/// Lo que cambia mueve solo el impuesto de su mes.
/// </summary>
public class RevisorDeEstatusSatWorker : AsyncPeriodicBackgroundWorkerBase
{
    private DateOnly? _ultimaNoche;

    public RevisorDeEstatusSatWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var hoy = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ActualizadorDeImpuestos.ZonaDeMexico);
        var dia = DateOnly.FromDateTime(hoy);
        if (hoy.Hour >= 5 || _ultimaNoche == dia)
        {
            return;
        }

        _ultimaNoche = dia;
        var r = await workerContext.ServiceProvider.GetRequiredService<RevisorDeEstatusSat>().RevisarLaNocheAsync(hoy);
        workerContext.ServiceProvider.GetRequiredService<ILogger<RevisorDeEstatusSatWorker>>().LogInformation(
            "Estatus del SAT: {Revisadas} revisadas, {Cambiaron} cambiaron, {Fallaron} fallaron, {Faltan} quedan para mañana.",
            r.Revisadas, r.Cambiaron, r.Fallaron, r.Faltan);
    }
}
