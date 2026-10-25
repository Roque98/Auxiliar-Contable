using Volo.Abp.Modularity;

namespace AuxiliarContable;

[DependsOn(
    typeof(AuxiliarContableDomainModule),
    typeof(AuxiliarContableTestBaseModule)
)]
public class AuxiliarContableDomainTestModule : AbpModule
{

}
