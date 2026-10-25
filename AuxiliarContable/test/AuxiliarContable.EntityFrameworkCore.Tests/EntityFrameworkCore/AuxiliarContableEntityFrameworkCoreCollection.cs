using Xunit;

namespace AuxiliarContable.EntityFrameworkCore;

[CollectionDefinition(AuxiliarContableTestConsts.CollectionDefinitionName)]
public class AuxiliarContableEntityFrameworkCoreCollection : ICollectionFixture<AuxiliarContableEntityFrameworkCoreFixture>
{

}
