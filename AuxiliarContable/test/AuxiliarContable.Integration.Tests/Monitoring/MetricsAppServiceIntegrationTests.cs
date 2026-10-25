using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Monitoring;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Tests de la feature de monitoreo contra PostgreSQL real. Lo que de verdad hay que
/// comprobar aquí no es el CRUD, sino la agregación del .sql: el cubo temporal, la media
/// ponderada y el conteo de muestras no sanas.
/// </summary>
public class MetricsAppServiceIntegrationTests : IntegrationTestBase
{
    private readonly IMetricsAppService _metricsAppService;
    private readonly IServerMetricRepository _serverMetricRepository;
    private readonly IGuidGenerator _guidGenerator;

    public MetricsAppServiceIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _metricsAppService = GetRequiredService<IMetricsAppService>();
        _serverMetricRepository = GetRequiredService<IServerMetricRepository>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    private async Task InsertarAsync(DateTime cuandoUtc, double cpu, int peticiones,
        int errores, double mediaMs, string salud = "Healthy",
        int erroresCliente = 0, int swapUsado = 0, int? uptime = 3600, int bloqueados = 0)
    {
        var muestra = new ServerMetric(_guidGenerator.Create(), cuandoUtc, salud)
            .SetSystem(cpu, 4096, 8192, swapUsado, 2048, 42.5)
            .SetApplication(peticiones, errores, erroresCliente, mediaMs, mediaMs * 2)
            .SetRateLimiting(bloqueados)
            .SetProcess(uptime);

        await WithUnitOfWorkAsync(() => _serverMetricRepository.InsertAsync(muestra, autoSave: true));
    }

    /// <summary>
    /// Instante fijo DENTRO de un mismo cubo de una hora.
    ///
    /// <para>Lo usan los tests que meten varias muestras y esperan verlas agregadas en UN
    /// punto. Antes partían de <c>DateTime.UtcNow</c> y colocaban las muestras a -5, -4 y
    /// -3 minutos: eso funciona 55 minutos de cada 60, pero si el test corre en los primeros
    /// minutos de una hora las muestras se reparten entre dos cubos y
    /// <c>Points.Last()</c> solo ve las últimas. Falló en CI un día a las 19:03 UTC.</para>
    ///
    /// <para>La hora anterior en el minuto 30 deja media hora de margen a cada lado, así que
    /// el resultado no depende de cuándo se ejecute.</para>
    /// </summary>
    private static DateTime AnclaEnUnCubo()
    {
        var ahora = DateTime.UtcNow;
        var enPunto = new DateTime(ahora.Year, ahora.Month, ahora.Day, ahora.Hour, 0, 0, DateTimeKind.Utc);
        return enPunto.AddHours(-1).AddMinutes(30);
    }

    [Fact]
    public async Task GetSeries_agrega_las_muestras_en_cubos_temporales()
    {
        var ahora = AnclaEnUnCubo();

        // Tres muestras dentro de la misma hora: con ventana de 168 h el cubo es de 60
        // minutos, así que las tres tienen que colapsar en un solo punto.
        await InsertarAsync(ahora.AddMinutes(-5), cpu: 10, peticiones: 100, errores: 1, mediaMs: 10);
        await InsertarAsync(ahora.AddMinutes(-4), cpu: 20, peticiones: 200, errores: 2, mediaMs: 20);
        await InsertarAsync(ahora.AddMinutes(-3), cpu: 30, peticiones: 300, errores: 3, mediaMs: 30);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 168 });

        serie.BucketMinutes.ShouldBe(60);

        var punto = serie.Points.Last();
        punto.RequestCount.ShouldBe(600);
        punto.ErrorCount.ShouldBe(6);
        punto.CpuPercent!.Value.ShouldBe(20, 0.01);   // media de 10, 20 y 30
    }

    [Fact]
    public async Task GetSeries_pondera_la_latencia_media_por_numero_de_peticiones()
    {
        var ahora = AnclaEnUnCubo();

        // Un minuto con 1 petición lentísima y otro con 999 rapidísimas. La media simple
        // daría ~500 ms y mentiría: casi nadie esperó eso.
        await InsertarAsync(ahora.AddMinutes(-5), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1000);
        await InsertarAsync(ahora.AddMinutes(-4), cpu: 1, peticiones: 999, errores: 0, mediaMs: 1);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 168 });

        var punto = serie.Points.Last();
        // (1000*1 + 1*999) / 1000 = 1.999
        punto.AvgResponseMs!.Value.ShouldBe(2, 0.01);
        punto.AvgResponseMs!.Value.ShouldBeLessThan(500);
    }

    [Fact]
    public async Task GetSeries_cuenta_las_muestras_no_sanas()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-3), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1);
        await InsertarAsync(ahora.AddMinutes(-2), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1, salud: "Unhealthy");

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 168 });

        serie.Points.Last().UnhealthySamples.ShouldBe(1);
    }

    [Fact]
    public async Task GetSeries_ignora_las_muestras_fuera_de_la_ventana()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddHours(-50), cpu: 99, peticiones: 5000, errores: 0, mediaMs: 1);
        await InsertarAsync(ahora.AddMinutes(-2), cpu: 1, peticiones: 7, errores: 0, mediaMs: 1);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 1 });

        serie.Points.Sum(p => p.RequestCount).ShouldBe(7);
    }

    [Fact]
    public async Task PurgeOlderThan_borra_de_verdad_las_muestras_viejas()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddDays(-40), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1);
        await InsertarAsync(ahora.AddMinutes(-1), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1);

        var borradas = await WithUnitOfWorkAsync(() =>
            _serverMetricRepository.PurgeOlderThanAsync(ahora.AddDays(-30)));

        borradas.ShouldBe(1);

        // Sin soft delete: la fila tiene que haber desaparecido de verdad.
        var quedan = await WithUnitOfWorkAsync(() => _serverMetricRepository.GetCountAsync());
        quedan.ShouldBe(1);
    }

    [Fact]
    public async Task GetSeries_detecta_un_reinicio_cuando_el_uptime_baja()
    {
        var ahora = DateTime.UtcNow;

        // El proceso llevaba 2 horas vivo y en la muestra siguiente lleva 30 segundos:
        // solo puede significar que se reinició entre las dos.
        await InsertarAsync(ahora.AddMinutes(-4), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1, uptime: 7200);
        await InsertarAsync(ahora.AddMinutes(-3), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1, uptime: 30);
        await InsertarAsync(ahora.AddMinutes(-2), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1, uptime: 90);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 168 });

        serie.Points.Sum(p => p.Restarts).ShouldBe(1);
    }

    [Fact]
    public async Task GetSeries_no_ve_reinicios_donde_el_uptime_solo_crece()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-3), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1, uptime: 100);
        await InsertarAsync(ahora.AddMinutes(-2), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1, uptime: 160);
        await InsertarAsync(ahora.AddMinutes(-1), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1, uptime: 220);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 168 });

        serie.Points.Sum(p => p.Restarts).ShouldBe(0);
    }

    [Fact]
    public async Task GetSeries_informa_de_la_antiguedad_de_la_ultima_muestra()
    {
        var ahora = DateTime.UtcNow;

        // Una muestra de hace 10 minutos: el recolector deberia haber escrito 10 veces
        // desde entonces, asi que la pagina tiene que poder decir que esta parado.
        await InsertarAsync(ahora.AddMinutes(-10), cpu: 1, peticiones: 1, errores: 0, mediaMs: 1);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 1 });

        serie.SampleIntervalSeconds.ShouldBe(60);
        serie.SecondsSinceLastSample.ShouldNotBeNull();
        serie.SecondsSinceLastSample!.Value.ShouldBeGreaterThan(9 * 60);
    }

    [Fact]
    public async Task GetSeries_separa_los_4xx_de_los_5xx()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-2), cpu: 1, peticiones: 100, errores: 2,
            mediaMs: 1, erroresCliente: 30);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 1 });

        var punto = serie.Points.Last();
        punto.ErrorCount.ShouldBe(2);
        punto.ClientErrorCount.ShouldBe(30);
    }

    [Fact]
    public async Task GetSeries_calcula_el_porcentaje_de_swap()
    {
        var ahora = DateTime.UtcNow;

        // 512 MB usados de 2048 = 25%.
        await InsertarAsync(ahora.AddMinutes(-2), cpu: 1, peticiones: 1, errores: 0,
            mediaMs: 1, swapUsado: 512);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 1 });

        serie.Points.Last().SwapPercent!.Value.ShouldBe(25, 0.01);
    }

    [Fact]
    public void ServerMetric_rechaza_una_fecha_que_no_sea_UTC()
    {
        // La columna es timestamptz y Npgsql lanza en tiempo de ejecución si el Kind no
        // es Utc. Mejor fallar al construir la entidad, donde el mensaje explica el motivo.
        Should.Throw<Volo.Abp.AbpException>(() =>
            new ServerMetric(_guidGenerator.Create(), DateTime.Now, "Healthy"));
    }

    [Fact]
    public async Task GetSeries_suma_los_bloqueos_por_rate_limiting()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-3), cpu: 1, peticiones: 10, errores: 0,
            mediaMs: 1, bloqueados: 5);
        await InsertarAsync(ahora.AddMinutes(-2), cpu: 1, peticiones: 10, errores: 0,
            mediaMs: 1, bloqueados: 7);

        var serie = await _metricsAppService.GetSeriesAsync(new GetMetricsInput { Hours = 1 });

        serie.Points.Sum(p => p.RateLimitedCount).ShouldBe(12);
    }

    [Fact]
    public void RateLimitEvent_rechaza_una_fecha_que_no_sea_UTC()
    {
        Should.Throw<Volo.Abp.AbpException>(() =>
            new RateLimitEvent(_guidGenerator.Create(), DateTime.Now, "1.2.3.4",
                "/api/app/product", 1, DateTime.UtcNow, DateTime.UtcNow));
    }
}
