using Volo.Abp.Modularity;

namespace AuxiliarContable;

[DependsOn(
    typeof(AuxiliarContableApplicationModule),
    typeof(AuxiliarContableDomainTestModule)
)]
public class AuxiliarContableApplicationTestModule : AbpModule
{

}
