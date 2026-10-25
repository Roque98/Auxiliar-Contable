using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.Data;

/* This is used if database provider does't define
 * IAuxiliarContableDbSchemaMigrator implementation.
 */
public class NullAuxiliarContableDbSchemaMigrator : IAuxiliarContableDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
