namespace AuxiliarContable.Settings;

public static class AuxiliarContableSettings
{
    private const string Prefix = "AuxiliarContable";

    /// <summary>
    /// Hasta qué versión de <c>PermisosNuevosPorVersion</c> se concedieron ya los permisos a
    /// los roles existentes. Interno: nadie lo edita.
    /// </summary>
    public const string VersionPermisosSembrados = Prefix + ".Permisos.VersionSembrada";
}
