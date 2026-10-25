using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Data;

/// <summary>
/// Fábrica de conexiones para <b>fuentes de datos externas de solo lectura</b>
/// (réplicas de reportes, bases legacy, instancias remotas). Devuelve una
/// conexión ya abierta lista para usar con Dapper, <b>fuera</b> del Unit of Work
/// y de la transacción de ABP.
/// <para>
/// NO usar para la base principal: para ella se usa EF Core / <c>SqlQueryRaw</c>
/// sobre el <c>DbContext</c> del Unit of Work (ver <c>IProductRepository</c>).
/// </para>
/// </summary>
public interface IExternalDataSourceConnectionFactory
{
    /// <summary>
    /// Abre una conexión a la fuente externa identificada por su nombre lógico
    /// (una constante de <c>DataSourceNames</c>). La cadena de conexión se
    /// resuelve con el resolver de ABP (<c>ConnectionStrings:{nombre}</c>,
    /// respetando multi-tenancy) y los metadatos desde <c>DataSources:{nombre}</c>.
    /// </summary>
    Task<DbConnection> CreateOpenConnectionAsync(
        string dataSourceName,
        CancellationToken cancellationToken = default);
}
