using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AuxiliarContable.Data;
using Volo.Abp.DependencyInjection;

namespace AuxiliarContable.EntityFrameworkCore;

public class EntityFrameworkCoreAuxiliarContableDbSchemaMigrator
    : IAuxiliarContableDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreAuxiliarContableDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the AuxiliarContableDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<AuxiliarContableDbContext>()
            .Database
            .MigrateAsync();
    }
}
