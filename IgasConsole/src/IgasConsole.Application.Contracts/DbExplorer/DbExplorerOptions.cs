namespace IgasConsole.DbExplorer;

/// <summary>
/// Habilitación del explorador de BD. Por defecto DESACTIVADO (seguro). El Host lo
/// activa solo en el entorno Development; los tests lo activan explícitamente.
/// </summary>
public class DbExplorerOptions
{
    public bool IsEnabled { get; set; }
}
