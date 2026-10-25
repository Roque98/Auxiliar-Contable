using AuxiliarContable.Samples;
using Xunit;

namespace AuxiliarContable.EntityFrameworkCore.Domains;

[Collection(AuxiliarContableTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<AuxiliarContableEntityFrameworkCoreTestModule>
{

}
