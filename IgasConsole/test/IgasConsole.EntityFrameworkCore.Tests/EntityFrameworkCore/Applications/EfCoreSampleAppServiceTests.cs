using IgasConsole.Samples;
using Xunit;

namespace IgasConsole.EntityFrameworkCore.Applications;

[Collection(IgasConsoleTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<IgasConsoleEntityFrameworkCoreTestModule>
{

}
