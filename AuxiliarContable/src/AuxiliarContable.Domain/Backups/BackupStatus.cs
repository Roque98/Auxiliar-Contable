using System;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Backups;

/// <summary>
/// Lo que la copia de seguridad dejó dicho la última vez que corrió.
///
/// <para>⚠️ La copia NO la hace la aplicación: la lanza un temporizador de systemd a las
/// 03:30, fuera del proceso. Sin este puente la aplicación no puede saber siquiera si
/// existe — y el fallo que hay que detectar es exactamente «lleva semanas rota y nadie se
/// ha enterado».</para>
///
/// <para>El puente es un archivo JSON en <c>/var/lib/auxiliar-contable</c>, que el script
/// escribe SIEMPRE, también al fallar. Se eligió un archivo y no una llamada a la API
/// porque la copia tiene que poder correr —y dejar constancia— aunque la aplicación esté
/// caída, que es justo cuando más importa.</para>
/// </summary>
public class BackupStatus
{
    public DateTime? LastRunUtc { get; set; }

    /// <summary>False si la última ejecución terminó con error.</summary>
    public bool Ok { get; set; }

    /// <summary>En qué punto se quedó: volcado, verificacion, remoto, completado…</summary>
    public string Stage { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }

    public long DumpBytes { get; set; }

    /// <summary>Tablas con datos dentro del volcado.</summary>
    public int Tables { get; set; }

    /// <summary>ok | sin-gancho | no-configurado. Distingue «no sube fuera» de «falló al subir».</summary>
    public string Remote { get; set; } = string.Empty;

    public int RemoteSnapshots { get; set; }

    /// <summary>Horas desde la última ejecución, o null si nunca corrió.</summary>
    public double? EdadHoras(DateTime ahoraUtc) =>
        LastRunUtc == null ? null : (ahoraUtc - LastRunUtc.Value).TotalHours;

    public bool EstaCaducada(DateTime ahoraUtc) =>
        EdadHoras(ahoraUtc) is null or > BackupConsts.MaxBackupAgeHours;
}

/// <summary>Lo que dejó dicho la verificación mensual por restauración.</summary>
public class RestoreCheckStatus
{
    public DateTime? LastRunUtc { get; set; }

    public bool Ok { get; set; }

    public string Stage { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }

    public int TablesRestored { get; set; }

    public int UsersRestored { get; set; }

    public string Message { get; set; } = string.Empty;

    public double? EdadDias(DateTime ahoraUtc) =>
        LastRunUtc == null ? null : (ahoraUtc - LastRunUtc.Value).TotalDays;

    public bool EstaCaducada(DateTime ahoraUtc) =>
        EdadDias(ahoraUtc) is null or > BackupConsts.MaxRestoreCheckAgeDays;
}

/// <summary>
/// Lee el estado que dejan los scripts. Devuelve null cuando no hay archivo —que es lo
/// normal en desarrollo, donde no hay copias— para que quien lo use distinga «no hay
/// información» de «la copia falló».
/// </summary>
public interface IBackupStatusReader
{
    Task<BackupStatus?> LeerCopiaAsync(CancellationToken cancellationToken = default);

    Task<RestoreCheckStatus?> LeerVerificacionAsync(CancellationToken cancellationToken = default);
}
