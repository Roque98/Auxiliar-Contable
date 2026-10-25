using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.Data;

/* This is used if database provider does't define
 * IIgasConsoleDbSchemaMigrator implementation.
 */
public class NullIgasConsoleDbSchemaMigrator : IIgasConsoleDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
