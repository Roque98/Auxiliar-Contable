namespace IgasConsole.DbExplorer;

/// <summary>Tabla del esquema (solo identificación; la política de edición la aplica la capa Application).</summary>
public class DbTableInfo
{
    public string Schema { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
