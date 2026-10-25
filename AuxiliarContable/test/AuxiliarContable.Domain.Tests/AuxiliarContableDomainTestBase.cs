using Volo.Abp.Modularity;

namespace AuxiliarContable;

/* Inherit from this class for your domain layer tests. */
public abstract class AuxiliarContableDomainTestBase<TStartupModule> : AuxiliarContableTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
