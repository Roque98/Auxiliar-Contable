using System;
using System.Threading.Tasks;
using AuxiliarContable.Monitoring;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Tests del listado de tráfico bloqueado por rate limiting, contra PostgreSQL real.
/// </summary>
public class RateLimitEventsIntegrationTests : IntegrationTestBase
{
    private readonly IMetricsAppService _metricsAppService;
    private readonly IRateLimitEventRepository _rateLimitEventRepository;
    private readonly IGuidGenerator _guidGenerator;

    public RateLimitEventsIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _metricsAppService = GetRequiredService<IMetricsAppService>();
        _rateLimitEventRepository = GetRequiredService<IRateLimitEventRepository>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    private async Task InsertarAsync(DateTime cuandoUtc, string ip, string endpoint, int veces)
    {
        var evento = new RateLimitEvent(
            _guidGenerator.Create(), cuandoUtc, ip, endpoint, veces, cuandoUtc, cuandoUtc);

        await WithUnitOfWorkAsync(() => _rateLimitEventRepository.InsertAsync(evento, autoSave: true));
    }

    [Fact]
    public async Task GetBlockedEvents_devuelve_los_eventos_recientes_ordenados_por_ultima_vez()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-10), "1.1.1.1", "/api/app/product", 3);
        await InsertarAsync(ahora.AddMinutes(-2), "2.2.2.2", "/api/app/product/{id}", 8);

        var eventos = await _metricsAppService.GetBlockedEventsAsync(
            new GetRateLimitEventsInput { Hours = 24 });

        eventos.Count.ShouldBe(2);
        eventos[0].IpAddress.ShouldBe("2.2.2.2");
        eventos[0].RejectionCount.ShouldBe(8);
        eventos[1].IpAddress.ShouldBe("1.1.1.1");
    }

    [Fact]
    public async Task GetBlockedEvents_respeta_la_ventana_de_horas()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddHours(-5), "3.3.3.3", "/api/app/product", 1);
        await InsertarAsync(ahora.AddMinutes(-2), "4.4.4.4", "/api/app/product", 1);

        var eventos = await _metricsAppService.GetBlockedEventsAsync(
            new GetRateLimitEventsInput { Hours = 1 });

        eventos.Count.ShouldBe(1);
        eventos[0].IpAddress.ShouldBe("4.4.4.4");
    }

    [Fact]
    public async Task PurgeOlderThan_borra_de_verdad_los_eventos_de_rate_limiting()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddDays(-40), "5.5.5.5", "/api/app/product", 1);
        await InsertarAsync(ahora.AddMinutes(-1), "6.6.6.6", "/api/app/product", 1);

        var borrados = await WithUnitOfWorkAsync(() =>
            _rateLimitEventRepository.PurgeOlderThanAsync(ahora.AddDays(-30)));

        borrados.ShouldBe(1);

        var quedan = await WithUnitOfWorkAsync(() => _rateLimitEventRepository.GetCountAsync());
        quedan.ShouldBe(1);
    }
}
