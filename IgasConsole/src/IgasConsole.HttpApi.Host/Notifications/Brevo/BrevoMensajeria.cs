using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.Notifications.Brevo;

/// <summary>SMS y WhatsApp por Brevo. Ver <see cref="IMensajeria"/>.</summary>
public class BrevoMensajeria : IMensajeria, ITransientDependency
{
    private readonly BrevoClient _brevo;
    private readonly BrevoOptions _opciones;
    private readonly ILogger<BrevoMensajeria> _logger;

    public BrevoMensajeria(BrevoClient brevo, IOptions<BrevoOptions> opciones, ILogger<BrevoMensajeria> logger)
    {
        _brevo = brevo;
        _opciones = opciones.Value;
        _logger = logger;
    }

    public bool EstaConfigurada => _opciones.TieneCredenciales;

    public Task EnviarSmsAsync(string telefono, string texto)
    {
        var numero = NumeroDeTelefono.Normalizar(telefono);
        return Descartado("SMS", numero) ? Task.CompletedTask : _brevo.EnviarSmsAsync(numero, texto);
    }

    public Task EnviarWhatsAppPlantillaAsync(string telefono, long plantillaId)
    {
        var numero = NumeroDeTelefono.Normalizar(telefono);
        return Descartado("WhatsApp", numero) ? Task.CompletedTask : _brevo.EnviarWhatsAppAsync(numero, plantillaId, null);
    }

    public Task EnviarWhatsAppAsync(string telefono, string texto)
    {
        var numero = NumeroDeTelefono.Normalizar(telefono);
        return Descartado("WhatsApp", numero) ? Task.CompletedTask : _brevo.EnviarWhatsAppAsync(numero, null, texto);
    }

    private bool Descartado(string canal, string numero)
    {
        if (EstaConfigurada)
        {
            return false;
        }

        // Solo los últimos dígitos: el log no es sitio para teléfonos completos.
        _logger.LogWarning("Brevo sin configurar: {Canal} a …{Final} NO se ha enviado.", canal, numero[^4..]);
        return true;
    }
}
