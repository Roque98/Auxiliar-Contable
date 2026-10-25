using Volo.Abp.Modularity;

namespace IgasConsole;

public abstract class IgasConsoleApplicationTestBase<TStartupModule> : IgasConsoleTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
