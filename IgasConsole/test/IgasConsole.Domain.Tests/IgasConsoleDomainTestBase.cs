using Volo.Abp.Modularity;

namespace IgasConsole;

/* Inherit from this class for your domain layer tests. */
public abstract class IgasConsoleDomainTestBase<TStartupModule> : IgasConsoleTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
