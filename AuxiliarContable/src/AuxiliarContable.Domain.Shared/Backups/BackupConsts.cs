namespace AuxiliarContable.Backups;

public static class BackupConsts
{
    /// <summary>
    /// A partir de cuántas horas sin copia se considera que algo va mal.
    /// <para>36 y no 24: la copia es diaria y el temporizador lleva un desfase aleatorio
    /// de hasta 15 minutos, así que un umbral de 24 h daría falsos avisos por rozar el
    /// límite. Con 36 h hace falta haberse saltado una copia entera.</para>
    /// </summary>
    public const int MaxBackupAgeHours = 36;

    /// <summary>
    /// A partir de cuántos días sin verificar por restauración se avisa.
    /// <para>45 sobre una verificación mensual: deja margen para que un mes largo o un
    /// reinicio a destiempo no disparen el aviso.</para>
    /// </summary>
    public const int MaxRestoreCheckAgeDays = 45;

    /// <summary>Cada cuánto mira el vigilante.</summary>
    public const int WatchIntervalHours = 6;

    /// <summary>Cuánto se calla tras avisar de lo mismo.</summary>
    public const int SilenceHours = 24;
}
