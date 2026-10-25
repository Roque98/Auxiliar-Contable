using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.Monitoring;
using Volo.Abp.DependencyInjection;

namespace IgasConsole;

/// <summary>
/// Doble de prueba de <see cref="IAlertNotifier"/>. Hace falta porque la implementación
/// real (TelegramAlertNotifier) vive en HttpApi.Host, y los tests de integración cargan
/// Application + EntityFrameworkCore pero NO el Host. Sin esto,
/// <c>MonitoringSettingsAppService</c> no se puede ni construir.
/// <para>No manda nada a ninguna parte: solo apunta lo que se le pidió enviar, por si
/// algún test quiere comprobarlo.</para>
/// </summary>
/// <remarks>
/// ExposeServices es OBLIGATORIO aquí: el registro por convención de ABP solo expone
/// automáticamente las interfaces cuyo nombre es el sufijo del de la clase
/// (<c>TelegramAlertNotifier</c> → <c>IAlertNotifier</c>). "AlertNotifierDePrueba" no
/// termina en "AlertNotifier", así que sin el atributo se registra la clase pero nadie
/// puede pedir <c>IAlertNotifier</c>.
/// </remarks>
[ExposeServices(typeof(IAlertNotifier))]
public class AlertNotifierDePrueba : IAlertNotifier, ISingletonDependency
{
    public List<(string Titulo, string Cuerpo)> Enviados { get; } = new();

    public Task NotifyAsync(string titulo, string cuerpo, CancellationToken cancellationToken = default)
    {
        Enviados.Add((titulo, cuerpo));
        return Task.CompletedTask;
    }
}
