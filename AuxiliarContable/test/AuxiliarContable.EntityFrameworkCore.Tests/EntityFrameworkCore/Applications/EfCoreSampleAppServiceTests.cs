using AuxiliarContable.Samples;
using Xunit;

namespace AuxiliarContable.EntityFrameworkCore.Applications;

[Collection(AuxiliarContableTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<AuxiliarContableEntityFrameworkCoreTestModule>
{

}
