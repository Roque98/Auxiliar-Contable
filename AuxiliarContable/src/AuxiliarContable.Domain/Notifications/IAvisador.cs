using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.Notifications;

/// <summary>
/// Un aviso operativo, por todos los canales a la vez.
///
/// <para>Existe para que «avisar» sea UNA llamada. Antes cada sitio que detectaba algo
/// encolaba el envío de Telegram por su cuenta; al añadir la campana, esos sitios habrían
/// pasado a hacer dos cosas cada uno, y el día que uno se olvidara de la segunda el aviso
/// existiría en un canal y no en el otro — sin que nada fallara.</para>
///
/// <para>Distinto de <see cref="Monitoring.IAlertNotifier"/>, que es solo el canal de
/// Telegram y vive por debajo de esto.</para>
/// </summary>
public interface IAvisador
{
    /// <summary>
    /// Deja constancia del aviso en la aplicación y lo encola para Telegram.
    /// </summary>
    /// <param name="permiso">Quién puede verlo dentro de la aplicación.</param>
    /// <param name="url">Ruta interna a la que lleva, o null.</param>
    Task AvisarAsync(
        string titulo,
        string cuerpo,
        NotificationSeverity severidad,
        string permiso,
        string? url = null,
        CancellationToken cancellationToken = default);
}
