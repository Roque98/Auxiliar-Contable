using System.Threading.Tasks;
using AuxiliarContable.EstadosDeCuenta;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace AuxiliarContable.BackgroundJobs;

/// <summary>Lee un estado de cuenta recién subido (ver <see cref="ProcesadorDeEstadoDeCuenta"/>).</summary>
public class LeerEstadoDeCuentaJob : AsyncBackgroundJob<LeerEstadoDeCuentaArgs>, ITransientDependency
{
    private readonly ProcesadorDeEstadoDeCuenta _procesador;
    private readonly IUnitOfWorkManager _unidadesDeTrabajo;

    public LeerEstadoDeCuentaJob(ProcesadorDeEstadoDeCuenta procesador, IUnitOfWorkManager unidadesDeTrabajo)
    {
        _procesador = procesador;
        _unidadesDeTrabajo = unidadesDeTrabajo;
    }

    public override async Task ExecuteAsync(LeerEstadoDeCuentaArgs args)
    {
        // La unidad de trabajo a mano: un trabajo no viene envuelto en una (ver EnviarAvisoDeMonitoreoJob).
        using var unidad = _unidadesDeTrabajo.Begin(requiresNew: true);

        // Sin try/catch: si el modelo está saturado, la excepción es la que hace que ABP lo
        // reintente más tarde. Lo que no tiene arreglo reintentando lo resuelve el procesador.
        await _procesador.ProcesarAsync(args.DocumentoId);

        await unidad.CompleteAsync();
    }
}
