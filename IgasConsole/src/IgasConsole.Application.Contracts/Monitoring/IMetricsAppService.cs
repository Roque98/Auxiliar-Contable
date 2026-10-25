using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace IgasConsole.Monitoring;

public interface IMetricsAppService : IApplicationService
{
    Task<MetricsSeriesDto> GetSeriesAsync(GetMetricsInput input);

    Task<List<RateLimitEventDto>> GetBlockedEventsAsync(GetRateLimitEventsInput input);

    /// <summary>Los mismos eventos bloqueados, en CSV y con los mismos filtros.</summary>
    Task<IRemoteStreamContent> GetBlockedEventsCsvAsync(GetRateLimitEventsInput input);
}
