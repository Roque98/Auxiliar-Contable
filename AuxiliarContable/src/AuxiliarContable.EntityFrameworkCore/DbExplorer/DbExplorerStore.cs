using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Data;
using AuxiliarContable.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace AuxiliarContable.DbExplorer;

/// <summary>
/// Implementación del explorador de BD (infraestructura). Construye SQL dinámico de
/// forma SEGURA: los identificadores (tabla/columna) se validan contra el esquema vivo
/// y se citan con comillas dobles (PostgreSQL); los valores SIEMPRE van por
/// <see cref="DbParameter"/>.
/// Trabaja sobre la conexión del <c>DbContext</c> del Unit of Work (enlaza su transacción),
/// no abre conexiones nuevas.
/// </summary>
public class DbExplorerStore : IDbExplorerStore, ITransientDependency
{
    private readonly IDbContextProvider<AuxiliarContableDbContext> _dbContextProvider;

    public DbExplorerStore(IDbContextProvider<AuxiliarContableDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task<List<DbTableInfo>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var sql = SqlResource.Load("DbExplorer/Sql/GetTables.sql");

        var result = new List<DbTableInfo>();
        await using var command = await CreateCommandAsync(dbContext, sql, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DbTableInfo { Schema = reader.GetString(0), Name = reader.GetString(1) });
        }
        return result;
    }

    public async Task<DbTableSchema?> GetSchemaAsync(string schema, string table, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var columns = new List<DbColumnInfo>();
        var columnsSql = SqlResource.Load("DbExplorer/Sql/GetColumns.sql");
        await using (var command = await CreateCommandAsync(dbContext, columnsSql, cancellationToken))
        {
            AddParameter(command, "@TableSchema", schema);
            AddParameter(command, "@TableName", table);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var dataType = reader.GetString(1);
                var isIdentity = !await reader.IsDBNullAsync(4, cancellationToken) && Convert.ToInt32(reader.GetValue(4)) == 1;
                var isComputed = !await reader.IsDBNullAsync(5, cancellationToken) && Convert.ToInt32(reader.GetValue(5)) == 1;
                columns.Add(new DbColumnInfo
                {
                    Name = reader.GetString(0),
                    DataType = dataType,
                    IsNullable = Convert.ToInt32(reader.GetValue(2)) == 1,
                    MaxLength = await reader.IsDBNullAsync(3, cancellationToken) ? null : Convert.ToInt32(reader.GetValue(3)),
                    IsReadOnly = isIdentity || isComputed
                });
            }
        }

        if (columns.Count == 0)
        {
            return null; // tabla inexistente / identificador no válido
        }

        var pkSql = SqlResource.Load("DbExplorer/Sql/GetPrimaryKeys.sql");
        await using (var command = await CreateCommandAsync(dbContext, pkSql, cancellationToken))
        {
            AddParameter(command, "@TableSchema", schema);
            AddParameter(command, "@TableName", table);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var pkName = reader.GetString(0);
                var col = columns.FirstOrDefault(c => c.Name == pkName);
                if (col != null)
                {
                    col.IsPrimaryKey = true;
                }
            }
        }

        return new DbTableSchema { Schema = schema, Name = table, Columns = columns };
    }

    public async Task<DbRowsResult> GetRowsAsync(
        DbTableSchema schema, int skipCount, int maxResultCount, string? sortColumn, bool sortDescending,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var qualified = QualifiedName(schema);
        var sortCol = ResolveSortColumn(schema, sortColumn);
        var selectList = string.Join(", ", schema.Columns.Select(c => QuoteId(c.Name)));

        var sql = $"SELECT {selectList} FROM {qualified} " +
                  $"ORDER BY {QuoteId(sortCol)} {(sortDescending ? "DESC" : "ASC")} " +
                  "LIMIT @take OFFSET @skip;";

        var rows = new List<Dictionary<string, object?>>();
        await using (var command = await CreateCommandAsync(dbContext, sql, cancellationToken))
        {
            AddParameter(command, "@skip", skipCount < 0 ? 0 : skipCount);
            AddParameter(command, "@take", maxResultCount <= 0 ? 50 : maxResultCount);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }
        }

        long total;
        var countSql = $"SELECT COUNT(*) FROM {qualified};";
        await using (var command = await CreateCommandAsync(dbContext, countSql, cancellationToken))
        {
            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            total = scalar is null or DBNull ? 0 : Convert.ToInt64(scalar);
        }

        return new DbRowsResult { Rows = rows, TotalCount = total };
    }

    public async Task InsertAsync(DbTableSchema schema, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var writable = values
            .Select(kv => (Col: RequireColumn(schema, kv.Key), kv.Value))
            .Where(x => !x.Col.IsReadOnly)
            .ToList();

        if (writable.Count == 0)
        {
            throw new AbpException("No hay columnas escribibles para insertar.");
        }

        var cols = string.Join(", ", writable.Select(w => QuoteId(w.Col.Name)));
        var placeholders = string.Join(", ", writable.Select((w, i) => Placeholder(w.Col, "@p" + i)));
        var sql = $"INSERT INTO {QualifiedName(schema)} ({cols}) VALUES ({placeholders});";

        await using var command = await CreateCommandAsync(dbContext, sql, cancellationToken);
        for (var i = 0; i < writable.Count; i++)
        {
            AddParameter(command, "@p" + i, CoerceValue(writable[i].Value, writable[i].Col));
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(DbTableSchema schema, IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        if (!schema.HasPrimaryKey)
        {
            throw new AbpException("La tabla no tiene clave primaria; no se puede actualizar de forma segura.");
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var setCols = values
            .Select(kv => (Col: RequireColumn(schema, kv.Key), kv.Value))
            .Where(x => !x.Col.IsReadOnly && !x.Col.IsPrimaryKey)
            .ToList();

        if (setCols.Count == 0)
        {
            throw new AbpException("No hay columnas escribibles para actualizar.");
        }

        var keyCols = BuildKey(schema, key);
        var setClause = string.Join(", ", setCols.Select((w, i) => $"{QuoteId(w.Col.Name)} = {Placeholder(w.Col, $"@s{i}")}"));
        var whereClause = string.Join(" AND ", keyCols.Select((w, i) => $"{QuoteId(w.Col.Name)} = @k{i}"));
        var sql = $"UPDATE {QualifiedName(schema)} SET {setClause} WHERE {whereClause};";

        await using var command = await CreateCommandAsync(dbContext, sql, cancellationToken);
        for (var i = 0; i < setCols.Count; i++)
        {
            AddParameter(command, "@s" + i, CoerceValue(setCols[i].Value, setCols[i].Col));
        }
        for (var i = 0; i < keyCols.Count; i++)
        {
            AddParameter(command, "@k" + i, CoerceValue(keyCols[i].Value, keyCols[i].Col));
        }

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new AbpException("No se actualizó ninguna fila (¿clave inexistente?).");
        }
    }

    public async Task DeleteAsync(DbTableSchema schema, IReadOnlyDictionary<string, object?> key, CancellationToken cancellationToken = default)
    {
        if (!schema.HasPrimaryKey)
        {
            throw new AbpException("La tabla no tiene clave primaria; no se puede borrar de forma segura.");
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var keyCols = BuildKey(schema, key);
        var whereClause = string.Join(" AND ", keyCols.Select((w, i) => $"{QuoteId(w.Col.Name)} = @k{i}"));
        var sql = $"DELETE FROM {QualifiedName(schema)} WHERE {whereClause};";

        await using var command = await CreateCommandAsync(dbContext, sql, cancellationToken);
        for (var i = 0; i < keyCols.Count; i++)
        {
            AddParameter(command, "@k" + i, CoerceValue(keyCols[i].Value, keyCols[i].Col));
        }

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new AbpException("No se borró ninguna fila (¿clave inexistente?).");
        }
    }

    // ---------------- helpers ----------------

    private static List<(DbColumnInfo Col, object? Value)> BuildKey(DbTableSchema schema, IReadOnlyDictionary<string, object?> key)
    {
        var result = new List<(DbColumnInfo, object?)>();
        foreach (var pk in schema.PrimaryKeyColumns)
        {
            var match = key.FirstOrDefault(k => string.Equals(k.Key, pk.Name, StringComparison.OrdinalIgnoreCase));
            if (match.Key == null)
            {
                throw new AbpException($"Falta el valor de la clave primaria '{pk.Name}'.");
            }
            result.Add((pk, match.Value));
        }
        return result;
    }

    private static DbColumnInfo RequireColumn(DbTableSchema schema, string name)
    {
        return schema.FindColumn(name)
               ?? throw new AbpException($"La columna '{name}' no existe en la tabla '{schema.Name}'.");
    }

    private static string ResolveSortColumn(DbTableSchema schema, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var col = schema.FindColumn(requested!) ?? throw new AbpException($"Columna de orden '{requested}' no válida.");
            return col.Name;
        }
        return schema.PrimaryKeyColumns.FirstOrDefault()?.Name ?? schema.Columns[0].Name;
    }

    private static string QualifiedName(DbTableSchema schema) => $"{QuoteId(schema.Schema)}.{QuoteId(schema.Name)}";

    /// <summary>
    /// Cita un identificador al estilo PostgreSQL. Es lo que impide que un nombre de
    /// tabla/columna (ya validado contra el esquema) se interprete como SQL, y ademas
    /// preserva la PascalCase que genera EF Core: sin comillas, PostgreSQL plegaria el
    /// identificador a minusculas y no encontraria la tabla.
    /// </summary>
    private static string QuoteId(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Placeholder del valor. Npgsql envia los string como <c>text</c>, y PostgreSQL no
    /// convierte text a json/jsonb implicitamente; para esas columnas hace falta un CAST
    /// explicito. El cast se aplica al PARAMETRO, no al valor: sigue sin haber concatenacion.
    /// </summary>
    private static string Placeholder(DbColumnInfo column, string parameterName) =>
        IsJsonType(column.DataType) ? $"CAST({parameterName} AS {column.DataType.ToLowerInvariant()})" : parameterName;

    private static bool IsJsonType(string dataType) =>
        string.Equals(dataType, "json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(dataType, "jsonb", StringComparison.OrdinalIgnoreCase);

    private static async Task<DbCommand> CreateCommandAsync(AuxiliarContableDbContext dbContext, string sql, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;

        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        return command;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Convierte el valor entrante (JsonElement/string/CLR) al tipo real de la columna.</summary>
    private static object? CoerceValue(object? raw, DbColumnInfo column)
    {
        if (raw == null)
        {
            return null;
        }

        object? source = raw;
        if (raw is JsonElement je)
        {
            switch (je.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.String:
                    source = je.GetString();
                    break;
                default:
                    source = je.GetRawText();
                    break;
            }
        }

        if (source is string s && s.Length == 0 && column.IsNullable)
        {
            return null;
        }

        // Los nombres son los de information_schema.columns de PostgreSQL ("integer",
        // "boolean", "timestamp without time zone"...), no los de SQL Server.
        var type = column.DataType.ToLowerInvariant();
        try
        {
            return type switch
            {
                "uuid" => source is Guid g ? g : Guid.Parse(AsString(source)),
                "boolean" => source is bool b ? b : IsTruthy(AsString(source)),
                "smallint" => Convert.ToInt16(source, CultureInfo.InvariantCulture),
                "integer" => Convert.ToInt32(source, CultureInfo.InvariantCulture),
                "bigint" => Convert.ToInt64(source, CultureInfo.InvariantCulture),
                "numeric" or "money" => Convert.ToDecimal(source, CultureInfo.InvariantCulture),
                "double precision" => Convert.ToDouble(source, CultureInfo.InvariantCulture),
                "real" => Convert.ToSingle(source, CultureInfo.InvariantCulture),
                "date" => source is DateTime d ? DateOnly.FromDateTime(d) : DateOnly.Parse(AsString(source), CultureInfo.InvariantCulture),
                // Npgsql exige Kind=Unspecified para 'timestamp without time zone' y UTC
                // para 'timestamp with time zone'; si no, lanza en tiempo de ejecucion.
                "timestamp without time zone" =>
                    DateTime.SpecifyKind(
                        source is DateTime dt ? dt : DateTime.Parse(AsString(source), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        DateTimeKind.Unspecified),
                "timestamp with time zone" =>
                    (source is DateTimeOffset dto ? dto : DateTimeOffset.Parse(AsString(source), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
                        .ToUniversalTime(),
                "time without time zone" => source is TimeSpan ts ? ts : TimeSpan.Parse(AsString(source), CultureInfo.InvariantCulture),
                "interval" => source is TimeSpan iv ? iv : TimeSpan.Parse(AsString(source), CultureInfo.InvariantCulture),
                "bytea" => Convert.FromBase64String(AsString(source)),
                // json/jsonb viajan como texto y el CAST del placeholder los convierte
                // (ver Placeholder); character varying, text, character y el resto, igual.
                _ => AsString(source)
            };
        }
        catch (Exception ex)
        {
            throw new AbpException($"Valor no válido para la columna '{column.Name}' ({column.DataType}): {ex.Message}");
        }
    }

    private static string AsString(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool IsTruthy(string value) =>
        value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
