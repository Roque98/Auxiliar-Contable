using System.Threading.Tasks;

namespace IgasConsole.Data;

public interface IIgasConsoleDbSchemaMigrator
{
    Task MigrateAsync();
}
