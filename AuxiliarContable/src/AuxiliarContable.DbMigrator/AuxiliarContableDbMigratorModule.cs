using AuxiliarContable.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace AuxiliarContable.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AuxiliarContableEntityFrameworkCoreModule),
    typeof(AuxiliarContableApplicationContractsModule)
)]
public class AuxiliarContableDbMigratorModule : AbpModule
{
}
