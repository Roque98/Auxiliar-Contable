using System;
using System.Collections.Generic;
using System.Linq;

namespace IgasConsole.DbExplorer;

/// <summary>Esquema completo de una tabla, obtenido de la introspección en vivo.</summary>
public class DbTableSchema
{
    public string Schema { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<DbColumnInfo> Columns { get; set; } = new();

    public IEnumerable<DbColumnInfo> PrimaryKeyColumns => Columns.Where(c => c.IsPrimaryKey);

    public bool HasPrimaryKey => Columns.Any(c => c.IsPrimaryKey);

    public DbColumnInfo? FindColumn(string name)
    {
        return Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
