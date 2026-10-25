using Volo.Abp.Modularity;

namespace IgasConsole;

[DependsOn(
    typeof(IgasConsoleApplicationModule),
    typeof(IgasConsoleDomainTestModule)
)]
public class IgasConsoleApplicationTestModule : AbpModule
{

}
