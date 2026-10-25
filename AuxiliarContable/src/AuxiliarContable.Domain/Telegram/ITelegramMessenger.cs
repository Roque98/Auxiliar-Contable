using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Telegram;

/// <summary>
/// Envía un mensaje a UN chat concreto. Distinto de <c>IAlertNotifier</c>, que manda los
/// avisos de monitoreo a los chats configurados globalmente: aquí el destinatario es una
/// persona, y el chat sale de su vínculo.
/// <para>La implementación vive en el host, que es quien tiene el token y el cliente HTTP.</para>
/// </summary>
public interface ITelegramMessenger
{
    /// <summary>Devuelve true si Telegram aceptó el mensaje. No lanza: quien llama decide
    /// qué hacer con un fallo de entrega.</summary>
    Task<bool> SendToChatAsync(long chatId, string texto, CancellationToken cancellationToken = default);

    /// <summary>Nombre de usuario del bot (sin @), necesario para construir el enlace
    /// t.me/&lt;bot&gt;?start=... Null si no hay bot configurado o Telegram no responde.
    /// <para>Puede tardar: la primera vez pregunta a Telegram. Usar SOLO donde el nombre
    /// haga falta de verdad, nunca para saber si hay bot (ver
    /// <see cref="IsBotConfiguredAsync"/>).</para></summary>
    Task<string?> GetBotUserNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ¿Hay un bot configurado? Mira SOLO los ajustes, sin salir a la red.
    /// <para>Existe porque preguntárselo a Telegram convertía una consulta de estado en
    /// algo que depende de que Telegram responda: en producción esa llamada llegó a
    /// tardar 15 segundos y dejaba la pantalla colgada.</para>
    /// </summary>
    Task<bool> IsBotConfiguredAsync(CancellationToken cancellationToken = default);
}
