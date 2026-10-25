namespace IgasConsole.DbExplorer;

/// <summary>Metadatos de una columna (introspección de esquema).</summary>
public class DbColumnInfo
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public bool IsNullable { get; set; }

    public bool IsPrimaryKey { get; set; }

    /// <summary>Identity, computed o rowversion: no editable.</summary>
    public bool IsReadOnly { get; set; }

    public int? MaxLength { get; set; }
}
