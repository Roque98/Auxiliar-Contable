using System.Threading;
using System.Threading.Tasks;

namespace IgasConsole.Monitoring;

public interface IAlertNotifier
{
    Task NotifyAsync(string titulo, string cuerpo, CancellationToken cancellationToken = default);
}
