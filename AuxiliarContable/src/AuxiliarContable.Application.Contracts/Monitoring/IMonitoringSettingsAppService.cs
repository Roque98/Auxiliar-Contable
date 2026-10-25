using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Monitoring;

public interface IMonitoringSettingsAppService : IApplicationService
{
    Task<MonitoringSettingsDto> GetAsync();

    Task<MonitoringSettingsDto> UpdateAsync(UpdateMonitoringSettingsDto input);

    /// <summary>Manda un mensaje de prueba, para no descubrir que el bot está mal
    /// configurado el día que de verdad se cae algo.</summary>
    Task<SendTestAlertResultDto> SendTestAsync();
}
