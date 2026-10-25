namespace IgasConsole.Monitoring;

public static class MonitoringConsts
{
    /// <summary>Cada cuánto toma una muestra el recolector.</summary>
    public const int SampleIntervalSeconds = 60;

    /// <summary>Días de histórico que se conservan. Lo viejo se borra de verdad (no soft delete).</summary>
    public const int RetentionDays = 30;

    public const int MaxHealthStatusLength = 32;

    public const string HealthUnknown = "Unknown";

    /// <summary>Muestras consecutivas fuera de rango antes de avisar. Sin esto, un pico
    /// de un minuto suena el móvil: la CPU al 91% durante 60 segundos no es una
    /// incidencia, es un backup o un despliegue.</summary>
    public const int MuestrasConsecutivasParaAvisar = 3;

    /// <summary>Longitud máxima de una IPv6 en texto (con zona incluida de sobra).</summary>
    public const int MaxIpAddressLength = 45;

    /// <summary>Se guarda el patrón de ruta ("/api/app/product/{id}"), no la URL con el
    /// id real: si no, cada petición distinta generaría una fila distinta.</summary>
    public const int MaxEndpointLength = 256;

}

/// <summary>
/// Nombres de los ajustes de monitoreo. Viven en la tabla AbpSettings y se editan desde
/// la web, no en archivos de configuración: así se cambian sin redesplegar.
/// </summary>
public static class MonitoringSettingNames
{
    private const string Prefijo = "IgasConsole.Monitoring.";

    /// <summary>Token del bot. Se guarda CIFRADO (isEncrypted en la definición).</summary>
    public const string TelegramBotToken = Prefijo + "TelegramBotToken";

    /// <summary>Uno o varios chat id separados por comas.</summary>
    public const string TelegramChatIds = Prefijo + "TelegramChatIds";

    public const string AlertsEnabled = Prefijo + "AlertsEnabled";

    /// <summary>Estado de las alertas activas, en JSON. Persistido a propósito: el worker
    /// vive en el proceso de la API, así que CADA DESPLIEGUE le borraba la memoria y
    /// volvía a avisar de un disco que seguía igual de lleno que hace una hora.</summary>
    public const string AlertState = Prefijo + "AlertState";

    /// <summary>
    /// Nombre de usuario del bot, cacheado. Preguntárselo a Telegram en cada consulta
    /// hacía que una pantalla dependiera de la latencia de un servicio externo; el nombre
    /// de un bot no cambia salvo que se cambie el bot, así que se guarda y se recalcula
    /// solo cuando se sustituye el token.
    /// </summary>
    public const string TelegramBotUserName = Prefijo + "TelegramBotUserName";

    /// <summary>Resultado del último envío, en JSON (ver <see cref="UltimoAvisoInfo"/>).
    /// Sin esto, que el canal de avisos deje de funcionar no se nota: no llega nada y
    /// todo parece normal.</summary>
    public const string LastAlertResult = Prefijo + "LastAlertResult";
}

/// <summary>
/// Forma en que se serializa <see cref="MonitoringSettingNames.LastAlertResult"/>.
/// <para>Vive en Domain.Shared porque es un contrato entre dos capas que no se
/// referencian: lo ESCRIBE el notificador (HttpApi.Host, que es quien sabe si el envío
/// salió bien) y lo LEE el application service que pinta la pantalla de ajustes.</para>
/// </summary>
public class UltimoAvisoInfo
{
    public System.DateTime Utc { get; set; }
    public bool Ok { get; set; }

    /// <summary>Motivo cuando falló. NUNCA lleva el token.</summary>
    public string? Detalle { get; set; }
}
