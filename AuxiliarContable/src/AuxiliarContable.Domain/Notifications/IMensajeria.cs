using System.Threading.Tasks;

namespace AuxiliarContable.Notifications;

/// <summary>
/// Mensajes al teléfono de una persona: SMS y WhatsApp. La implementación va por Brevo
/// (<c>BrevoMensajeria</c>, en el host); el correo NO pasa por aquí, va por el
/// <c>IEmailSender</c> de ABP, que es lo que ya usan sus módulos (recuperar contraseña,
/// confirmar correo).
///
/// <para>Los números van con el código de país y sin el «+» (<c>5215512345678</c>), que es
/// el formato que pide Brevo. <see cref="NumeroDeTelefono.Normalizar"/> lo limpia.</para>
/// </summary>
public interface IMensajeria
{
    /// <summary>¿Hay credenciales? Sin ellas los envíos se registran y se descartan.</summary>
    bool EstaConfigurada { get; }

    Task EnviarSmsAsync(string telefono, string texto);

    /// <summary>
    /// ⚠️ WhatsApp solo deja escribir primero con una <b>plantilla aprobada por Meta</b>. El
    /// texto libre (<see cref="EnviarWhatsAppAsync"/>) solo llega si la persona escribió en
    /// las últimas 24 horas.
    /// </summary>
    Task EnviarWhatsAppPlantillaAsync(string telefono, long plantillaId);

    Task EnviarWhatsAppAsync(string telefono, string texto);
}
