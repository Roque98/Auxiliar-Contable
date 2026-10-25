using IgasConsole.Security;
using Volo.Abp.AspNetCore.WebClientInfo;
using Volo.Abp.DependencyInjection;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Implementación sobre <see cref="IWebClientInfoProvider"/> de ABP, que es el mismo del
/// que sale la IP en el registro de auditoría — así ambos ven la misma dirección.
/// </summary>
public class CallerIpProvider : ICallerIpProvider, ITransientDependency
{
    private readonly IWebClientInfoProvider _webClientInfo;

    public CallerIpProvider(IWebClientInfoProvider webClientInfo)
    {
        _webClientInfo = webClientInfo;
    }

    public string? IpActual => _webClientInfo.ClientIpAddress;

    public string? AgenteActual => _webClientInfo.BrowserInfo;
}
