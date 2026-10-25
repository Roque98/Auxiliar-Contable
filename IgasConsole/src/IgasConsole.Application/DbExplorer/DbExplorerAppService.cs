using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;

namespace IgasConsole.DbExplorer;

/// <summary>
/// Explorador de BD. Solo Development + permiso. Lectura de todas las tablas;
/// edición SOLO de tablas de la app (prefijo <see cref="IgasConsoleConsts.DbTablePrefix"/>).
/// Delega el SQL dinámico seguro en <see cref="IDbExplorerStore"/> (capa de infraestructura).
/// </summary>
[Authorize(IgasConsolePermissions.DbExplorer.Default)]
public class DbExplorerAppService : IgasConsoleAppService, IDbExplorerAppService
{
    private readonly IDbExplorerStore _store;
    private readonly DbExplorerOptions _options;

    public DbExplorerAppService(IDbExplorerStore store, IOptions<DbExplorerOptions> options)
    {
        _store = store;
        _options = options.Value;
    }

    public async Task<List<TableInfoDto>> GetTablesAsync()
    {
        EnsureEnabled();

        var tables = await _store.GetTablesAsync();
        return tables
            .Select(t => new TableInfoDto
            {
                Schema = t.Schema,
                Name = t.Name,
                IsEditable = IsEditable(t.Name)
            })
            .ToList();
    }

    public async Task<TableSchemaDto> GetSchemaAsync(string schema, string table)
    {
        EnsureEnabled();

        var domainSchema = await GetSchemaOrThrowAsync(schema, table);
        return MapSchema(domainSchema);
    }

    public async Task<TableRowsDto> GetRowsAsync(GetRowsInput input)
    {
        EnsureEnabled();

        var domainSchema = await GetSchemaOrThrowAsync(input.Schema, input.Table);
        var result = await _store.GetRowsAsync(
            domainSchema,
            input.SkipCount,
            input.MaxResultCount,
            input.SortColumn,
            input.SortDescending);

        return new TableRowsDto
        {
            Columns = MapColumns(domainSchema),
            Rows = result.Rows,
            TotalCount = result.TotalCount
        };
    }

    [Authorize(IgasConsolePermissions.DbExplorer.Edit)]
    public async Task InsertAsync(InsertRowInput input)
    {
        EnsureEnabled();

        var domainSchema = await GetSchemaOrThrowAsync(input.Schema, input.Table);
        EnsureEditable(domainSchema);
        await _store.InsertAsync(domainSchema, input.Values);
    }

    [Authorize(IgasConsolePermissions.DbExplorer.Edit)]
    public async Task UpdateAsync(UpdateRowInput input)
    {
        EnsureEnabled();

        var domainSchema = await GetSchemaOrThrowAsync(input.Schema, input.Table);
        EnsureEditable(domainSchema);
        await _store.UpdateAsync(domainSchema, input.Key, input.Values);
    }

    [Authorize(IgasConsolePermissions.DbExplorer.Edit)]
    public async Task DeleteAsync(DeleteRowInput input)
    {
        EnsureEnabled();

        var domainSchema = await GetSchemaOrThrowAsync(input.Schema, input.Table);
        EnsureEditable(domainSchema);
        await _store.DeleteAsync(domainSchema, input.Key);
    }

    // ---------------- helpers ----------------

    private void EnsureEnabled()
    {
        if (!_options.IsEnabled)
        {
            throw new UserFriendlyException("El explorador de BD solo está disponible en el entorno de Development.");
        }
    }

    private async Task<DbTableSchema> GetSchemaOrThrowAsync(string schema, string table)
    {
        var normalizedSchema = string.IsNullOrWhiteSpace(schema) ? DbExplorerConsts.DefaultSchema : schema;
        var domainSchema = await _store.GetSchemaAsync(normalizedSchema, table);
        if (domainSchema == null)
        {
            throw new UserFriendlyException($"La tabla '{normalizedSchema}.{table}' no existe.");
        }
        return domainSchema;
    }

    private void EnsureEditable(DbTableSchema schema)
    {
        if (!IsEditable(schema.Name))
        {
            throw new UserFriendlyException(
                $"La tabla '{schema.Name}' es de solo lectura. Solo se pueden editar las tablas de la aplicación " +
                $"(prefijo '{IgasConsoleConsts.DbTablePrefix}').");
        }
    }

    private static bool IsEditable(string tableName)
    {
        return tableName.StartsWith(IgasConsoleConsts.DbTablePrefix, StringComparison.Ordinal);
    }

    private static TableSchemaDto MapSchema(DbTableSchema schema)
    {
        return new TableSchemaDto
        {
            Schema = schema.Schema,
            Name = schema.Name,
            IsEditable = IsEditable(schema.Name),
            Columns = MapColumns(schema)
        };
    }

    private static List<ColumnInfoDto> MapColumns(DbTableSchema schema)
    {
        return schema.Columns
            .Select(c => new ColumnInfoDto
            {
                Name = c.Name,
                DataType = c.DataType,
                IsNullable = c.IsNullable,
                IsPrimaryKey = c.IsPrimaryKey,
                IsReadOnly = c.IsReadOnly,
                MaxLength = c.MaxLength
            })
            .ToList();
    }
}
