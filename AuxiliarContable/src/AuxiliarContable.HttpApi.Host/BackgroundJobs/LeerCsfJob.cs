using System.Threading.Tasks;
using AuxiliarContable.PerfilesFiscales;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace AuxiliarContable.BackgroundJobs;

/// <summary>Lee una CSF recién subida (ver <see cref="ProcesadorDeCsf"/>).</summary>
public class LeerCsfJob : AsyncBackgroundJob<LeerCsfArgs>, ITransientDependency
{
    private readonly ProcesadorDeCsf _procesador;
    private readonly IUnitOfWorkManager _unidadesDeTrabajo;

    public LeerCsfJob(ProcesadorDeCsf procesador, IUnitOfWorkManager unidadesDeTrabajo)
    {
        _procesador = procesador;
        _unidadesDeTrabajo = unidadesDeTrabajo;
    }

    public override async Task ExecuteAsync(LeerCsfArgs args)
    {
        // La unidad de trabajo a mano: un trabajo no viene envuelto en una (ver EnviarAvisoDeMonitoreoJob).
        using var unidad = _unidadesDeTrabajo.Begin(requiresNew: true);

        // Sin try/catch: si el modelo está saturado, la excepción es la que hace que ABP lo
        // reintente más tarde. Lo que no tiene arreglo reintentando lo resuelve el procesador.
        await _procesador.ProcesarAsync(args.DocumentoId);

        await unidad.CompleteAsync();
    }
}
