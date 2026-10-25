using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.DbExplorer;

/// <summary>
/// Acceso genérico de solo-infraestructura a la base principal para el explorador de BD.
/// Toda la construcción de SQL dinámico vive en la implementación (capa EF Core), que
/// valida los identificadores contra el esquema vivo y parametriza los valores.
/// </summary>
public interface IDbExplorerStore
{
    Task<List<DbTableInfo>> GetTablesAsync(CancellationToken cancellationToken = default);

    /// <summary>Devuelve el esquema de la tabla, o null si no existe (identificador no válido).</summary>
    Task<DbTableSchema?> GetSchemaAsync(string schema, string table, CancellationToken cancellationToken = default);

    Task<DbRowsResult> GetRowsAsync(
        DbTableSchema schema,
        int skipCount,
        int maxResultCount,
        string? sortColumn,
        bool sortDescending,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        DbTableSchema schema,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        DbTableSchema schema,
        IReadOnlyDictionary<string, object?> key,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        DbTableSchema schema,
        IReadOnlyDictionary<string, object?> key,
        CancellationToken cancellationToken = default);
}

/// <summary>Resultado paginado de una lectura genérica.</summary>
public class DbRowsResult
{
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    public long TotalCount { get; set; }
}
