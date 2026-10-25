using System;
using IgasConsole.Notifications;

namespace IgasConsole.Backups;

/// <summary>
/// Cuándo merece la pena avisar del estado de las copias.
///
/// <para>Vive en el dominio y no en el worker a propósito. El worker solo lee archivos y
/// escribe avisos; <b>aquí</b> está el criterio, y el criterio es lo que puede estar mal.
/// Separado se puede probar sin tocar disco, base de datos ni systemd — y de hecho tiene
/// más tests que código.</para>
///
/// <para>Las dos averías que evita son simétricas: callarse cuando la copia lleva semanas
/// rota, y avisar tan a menudo que nadie lea los avisos.</para>
/// </summary>
public static class BackupAlertRules
{
    /// <summary>
    /// Devuelve el aviso que toca, o título null si no hay nada que contar.
    /// <para>El orden importa: se informa de lo MÁS grave y se para. Un aviso por cada
    /// cosa rota sería una avalancha justo cuando hace falta claridad.</para>
    /// </summary>
    public static (string? Titulo, string? Cuerpo, NotificationSeverity Severidad) Evaluar(
        BackupStatus? copia, RestoreCheckStatus? verificacion, DateTime ahoraUtc)
    {
        if (copia == null)
        {
            // Ni un archivo de estado. O nunca ha corrido, o alguien desmontó el
            // temporizador. Lo segundo es exactamente el fallo silencioso a detectar:
            // sin este caso, "no hay noticias" parecería buena noticia.
            return ("No hay ninguna copia de seguridad registrada",
                "La aplicación no encuentra el estado de las copias. O nunca se ha ejecutado " +
                "la copia, o el temporizador de systemd ya no está activo.",
                NotificationSeverity.Error);
        }

        if (!copia.Ok)
        {
            return ($"La copia de seguridad está fallando (etapa «{copia.Stage}»)",
                $"El último intento fue el {copia.LastRunUtc:yyyy-MM-dd HH:mm} UTC y no terminó. " +
                "Mientras siga así no se está guardando nada nuevo.",
                NotificationSeverity.Error);
        }

        if (copia.EstaCaducada(ahoraUtc))
        {
            var edad = copia.EdadHoras(ahoraUtc);
            return ("La copia de seguridad lleva demasiado tiempo sin hacerse",
                $"La última correcta es de hace {edad:F0} horas, y debería haber una cada día. " +
                "Puede que el temporizador esté parado o que el servidor estuviera apagado.",
                NotificationSeverity.Error);
        }

        if (copia.Remote != "ok")
        {
            return ("La copia no está saliendo del servidor",
                $"La copia local se hace bien, pero la subida al destino remoto está en estado " +
                $"«{copia.Remote}». Protege contra un borrado accidental, no contra perder la máquina.",
                NotificationSeverity.Error);
        }

        // Lo de abajo es menos urgente: hay copias recientes y salen fuera. Lo que falla
        // es la garantía de que SIRVEN.
        if (verificacion is { Ok: false })
        {
            return ("La copia no se pudo restaurar en la última verificación",
                $"Se sube correctamente, pero al restaurarla falló: {verificacion.Message}. " +
                "Una copia que no restaura no es una copia.",
                NotificationSeverity.Error);
        }

        if (verificacion != null && verificacion.EstaCaducada(ahoraUtc))
        {
            var edad = verificacion.EdadDias(ahoraUtc);
            // Warning y no Error: es una garantía que ha caducado, no una avería. Igualar
            // su urgencia a la de una copia que no se hace haría que el color dejara de
            // informar.
            return ("Hace tiempo que no se comprueba que la copia se pueda restaurar",
                $"La última verificación fue hace {edad:F0} días. Debería hacerse cada mes.",
                NotificationSeverity.Warning);
        }

        // Sin verificación todavía y con la copia bien: recién montado el sistema aún no
        // ha corrido la mensual. Avisar el primer día sería ruido por algo que se arregla
        // solo.
        return (null, null, NotificationSeverity.Info);
    }
}
