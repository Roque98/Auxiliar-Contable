using System.Threading.Tasks;

namespace AuxiliarContable.Data;

public interface IAuxiliarContableDbSchemaMigrator
{
    Task MigrateAsync();
}
