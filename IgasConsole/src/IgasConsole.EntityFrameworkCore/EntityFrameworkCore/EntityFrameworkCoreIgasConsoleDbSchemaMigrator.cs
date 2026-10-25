using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IgasConsole.Data;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.EntityFrameworkCore;

public class EntityFrameworkCoreIgasConsoleDbSchemaMigrator
    : IIgasConsoleDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreIgasConsoleDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the IgasConsoleDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<IgasConsoleDbContext>()
            .Database
            .MigrateAsync();
    }
}
