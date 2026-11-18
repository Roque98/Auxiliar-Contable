using System;
using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Cada minuto: recalcula los impuestos del mes que quedaron «por actualizar» y llevan 10 minutos sin cambios (ver
/// <see cref="ActualizadorDeImpuestos"/>), y la primera vuelta después de las 12 de la noche (hora de México) pide el mes
/// en curso y el anterior de todos los clientes. No usa Claude: el cálculo es del motor (tarifas y sumas).
/// </summary>
public class ActualizadorDeImpuestosWorker : AsyncPeriodicBackgroundWorkerBase
{
    private DateOnly? _ultimaNoche;

    public ActualizadorDeImpuestosWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var actualizador = workerContext.ServiceProvider.GetRequiredService<ActualizadorDeImpuestos>();
        var hoy = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ActualizadorDeImpuestos.ZonaDeMexico);
        var dia = DateOnly.FromDateTime(hoy);
        // Al arrancar no se dispara: solo cuando cambia el día estando en marcha.
        if (_ultimaNoche == null)
        {
            _ultimaNoche = dia;
        }
        else if (_ultimaNoche != dia)
        {
            _ultimaNoche = dia;
            await actualizador.PedirLaVueltaDeLaNocheAsync(hoy);
        }

        await actualizador.ActualizarAsync();
    }
}
