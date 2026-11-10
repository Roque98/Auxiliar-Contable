using System.Threading.Tasks;
using AuxiliarContable.Contabilidad;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace AuxiliarContable.BackgroundJobs;

/// <summary>
/// Pone en su mes un estado de cuenta que se subió sin decirlo (ver <see cref="FechadorDeEstadosDeCuenta"/>).
/// Con el motor caído lanza y ABP lo reintenta más tarde.
/// </summary>
public class FecharEstadoDeCuentaJob : AsyncBackgroundJob<FecharEstadoDeCuentaArgs>, ITransientDependency
{
    private readonly FechadorDeEstadosDeCuenta _fechador;
    private readonly IUnitOfWorkManager _unidadesDeTrabajo;

    public FecharEstadoDeCuentaJob(FechadorDeEstadosDeCuenta fechador, IUnitOfWorkManager unidadesDeTrabajo)
    {
        _fechador = fechador;
        _unidadesDeTrabajo = unidadesDeTrabajo;
    }

    public override async Task ExecuteAsync(FecharEstadoDeCuentaArgs args)
    {
        using var uow = _unidadesDeTrabajo.Begin(requiresNew: true);
        await _fechador.FecharAsync(args.DocumentoId);
        await uow.CompleteAsync();
    }
}
