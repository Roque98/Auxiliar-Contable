using Xunit;

namespace IgasConsole.EntityFrameworkCore;

[CollectionDefinition(IgasConsoleTestConsts.CollectionDefinitionName)]
public class IgasConsoleEntityFrameworkCoreCollection : ICollectionFixture<IgasConsoleEntityFrameworkCoreFixture>
{

}
