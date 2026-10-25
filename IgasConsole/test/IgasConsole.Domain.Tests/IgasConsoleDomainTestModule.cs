using Volo.Abp.Modularity;

namespace IgasConsole;

[DependsOn(
    typeof(IgasConsoleDomainModule),
    typeof(IgasConsoleTestBaseModule)
)]
public class IgasConsoleDomainTestModule : AbpModule
{

}
