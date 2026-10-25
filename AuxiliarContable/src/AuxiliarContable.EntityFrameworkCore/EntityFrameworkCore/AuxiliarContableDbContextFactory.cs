using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AuxiliarContable.EntityFrameworkCore;

/* This class is needed for EF Core console commands
 * (like Add-Migration and Update-Database commands) */
public class AuxiliarContableDbContextFactory : IDesignTimeDbContextFactory<AuxiliarContableDbContext>
{
    public AuxiliarContableDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        
        AuxiliarContableEfCoreEntityExtensionMappings.Configure();

        var builder = new DbContextOptionsBuilder<AuxiliarContableDbContext>()
            .UseNpgsql(configuration.GetConnectionString("Default"));
        
        return new AuxiliarContableDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../AuxiliarContable.DbMigrator/"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        return builder.Build();
    }
}
