using Volo.Abp.Modularity;

namespace AuxiliarContable;

public abstract class AuxiliarContableApplicationTestBase<TStartupModule> : AuxiliarContableTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
