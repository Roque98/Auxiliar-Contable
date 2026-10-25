namespace IgasConsole.Backups;

/// <summary>
/// Dónde están los archivos de estado que dejan los scripts de copia.
///
/// <para>Van en <c>appsettings</c> y no en ajustes editables: son rutas del sistema de
/// archivos, no algo que se cambie desde una pantalla. En desarrollo no existen y el
/// lector devuelve null, que es lo correcto — en una máquina de desarrollo no hay copias
/// y decir que «fallaron» sería mentir.</para>
/// </summary>
public class BackupOptions
{
    public string StatusFilePath { get; set; } = "/var/lib/zia-console/backup-status.json";

    public string RestoreCheckFilePath { get; set; } =
        "/var/lib/zia-console/restore-check-status.json";
}
