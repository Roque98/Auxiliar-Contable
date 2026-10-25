using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace AuxiliarContable.DbExplorer;

/// <summary>Tabla listada por el explorador. <see cref="IsEditable"/> = tabla de la app (prefijo App).</summary>
public class TableInfoDto
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEditable { get; set; }
}

public class ColumnInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsReadOnly { get; set; }
    public int? MaxLength { get; set; }
}

public class TableSchemaDto
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEditable { get; set; }
    public List<ColumnInfoDto> Columns { get; set; } = new();
}

public class GetRowsInput : PagedAndSortedResultRequestDto
{
    public string Schema { get; set; } = DbExplorerConsts.DefaultSchema;
    public string Table { get; set; } = string.Empty;
    public string? SortColumn { get; set; }
    public bool SortDescending { get; set; }
}

public class TableRowsDto
{
    public List<ColumnInfoDto> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public long TotalCount { get; set; }
}

public class InsertRowInput
{
    public string Schema { get; set; } = DbExplorerConsts.DefaultSchema;
    public string Table { get; set; } = string.Empty;
    public Dictionary<string, object?> Values { get; set; } = new();
}

public class UpdateRowInput
{
    public string Schema { get; set; } = DbExplorerConsts.DefaultSchema;
    public string Table { get; set; } = string.Empty;
    public Dictionary<string, object?> Key { get; set; } = new();
    public Dictionary<string, object?> Values { get; set; } = new();
}

public class DeleteRowInput
{
    public string Schema { get; set; } = DbExplorerConsts.DefaultSchema;
    public string Table { get; set; } = string.Empty;
    public Dictionary<string, object?> Key { get; set; } = new();
}
