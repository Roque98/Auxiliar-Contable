using IgasConsole.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace IgasConsole.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(IgasConsoleEntityFrameworkCoreModule),
    typeof(IgasConsoleApplicationContractsModule)
)]
public class IgasConsoleDbMigratorModule : AbpModule
{
}
