using Volo.Abp.Auditing;
using System.ComponentModel.DataAnnotations;

namespace AuxiliarContable.Monitoring;

public class MonitoringSettingsDto
{
    public bool AlertsEnabled { get; set; }

    /// <summary>
    /// NUNCA el token real: solo si hay uno guardado y sus últimos caracteres, para que
    /// se pueda reconocer sin exponerlo. Un secreto que la API devuelve es un secreto que
    /// acaba en el historial del navegador, en un log o en una captura de pantalla.
    /// </summary>
    public bool HasBotToken { get; set; }

    public string? BotTokenHint { get; set; }

    public string? ChatIds { get; set; }

    /// <summary>
    /// Resultado del último intento de envío. Existe porque un canal de avisos que falla
    /// en silencio es peor que no tenerlo: la página parece sana, nadie recibe nada y
    /// nadie se entera. Null = todavía no se ha intentado ninguno.
    /// </summary>
    public System.DateTime? LastAlertUtc { get; set; }

    public bool? LastAlertOk { get; set; }

    /// <summary>Motivo cuando falló. Nunca lleva el token.</summary>
    public string? LastAlertDetail { get; set; }
}

public class UpdateMonitoringSettingsDto
{
    /// <summary>
    /// NULLABLE A PROPÓSITO, igual que <see cref="BotToken"/>: null significa "no toques
    /// esto", no "apágalo".
    /// <para>Cuando era <c>bool</c> a secas, guardar el formulario para cambiar el token
    /// enviaba también el interruptor con el valor que tuviera cargado la página. Si esa
    /// página llevaba un rato abierta, ese valor estaba obsoleto y APAGABA los avisos sin
    /// que nadie lo pidiera — y en silencio, porque un canal de avisos apagado se ve
    /// igual que uno que no tiene nada que decir. Pasó en producción.</para>
    /// </summary>
    public bool? AlertsEnabled { get; set; }

    /// <summary>Dejar vacío conserva el token que ya hubiera guardado.</summary>
    [MaxLength(256)]
    // Fuera del registro de auditoría: ABP guarda los parámetros de cada llamada.
    [DisableAuditing]
    public string? BotToken { get; set; }

    /// <summary>Uno o varios chat id separados por comas.</summary>
    [MaxLength(512)]
    [RegularExpression(@"^\s*$|^-?\d+(\s*,\s*-?\d+)*$",
        ErrorMessage = "Los chat id son números separados por comas (los de grupo empiezan por -).")]
    public string? ChatIds { get; set; }
}

public class SendTestAlertResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
