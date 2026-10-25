using IgasConsole.Samples;
using Xunit;

namespace IgasConsole.EntityFrameworkCore.Domains;

[Collection(IgasConsoleTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<IgasConsoleEntityFrameworkCoreTestModule>
{

}
