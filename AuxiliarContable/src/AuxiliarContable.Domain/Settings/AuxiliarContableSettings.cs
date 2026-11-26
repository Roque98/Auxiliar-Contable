namespace AuxiliarContable.Settings;

public static class AuxiliarContableSettings
{
    private const string Prefix = "AuxiliarContable";

    /// <summary>
    /// Hasta qué versión de <c>PermisosNuevosPorVersion</c> se concedieron ya los permisos a
    /// los roles existentes. Interno: nadie lo edita.
    /// </summary>
    public const string VersionPermisosSembrados = Prefix + ".Permisos.VersionSembrada";

    /// <summary>
    /// El chat con IA (API de Claude). Apagado, la consola se usa solo por pantallas: el menú y la
    /// portada no lo enseñan y el servicio lo rechaza. Se enciende sin desplegar código con
    /// <c>Asistente__Habilitado=true</c> en el env (systemd ignora los nombres con punto; alias en Program.cs) (y la API key de Claude).
    /// </summary>
    public const string AsistenteHabilitado = Prefix + ".Asistente.Habilitado";
}
