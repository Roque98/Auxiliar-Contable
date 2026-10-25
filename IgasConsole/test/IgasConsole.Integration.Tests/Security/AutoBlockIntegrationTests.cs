using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Monitoring;
using IgasConsole.Security;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace IgasConsole;

/// <summary>
/// La fuente de IPs abusivas, que es de donde sale la decisión de bloquear solo.
/// <para>El worker vive en HttpApi.Host y estos tests no lo cargan, así que lo que se
/// prueba aquí es la consulta que alimenta la regla: si esa cuenta mal, el bloqueo
/// automático bloquea a quien no debe.</para>
/// </summary>
public class AutoBlockIntegrationTests : IntegrationTestBase
{
    private readonly IAbusiveIpSource _fuente;
    private readonly IRateLimitEventRepository _eventos;
    private readonly IGuidGenerator _guids;

    public AutoBlockIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _fuente = GetRequiredService<IAbusiveIpSource>();
        _eventos = GetRequiredService<IRateLimitEventRepository>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private Task SembrarAsync(string ip, int rechazos, int haceMinutos)
    {
        var cuando = DateTime.UtcNow.AddMinutes(-haceMinutos);

        return WithUnitOfWorkAsync(() => _eventos.InsertAsync(
            new RateLimitEvent(_guids.Create(), cuando, ip, "/api/app/product", rechazos, cuando, cuando),
            autoSave: true));
    }

    [Fact]
    public async Task Suma_los_rechazos_de_una_misma_IP()
    {
        // Tres rollups distintos (minutos y endpoints distintos) de la misma direccion.
        await SembrarAsync("203.0.113.1", 40, 1);
        await SembrarAsync("203.0.113.1", 40, 2);
        await SembrarAsync("203.0.113.1", 40, 3);

        var abusivas = await WithUnitOfWorkAsync(
            () => _fuente.GetAsync(DateTime.UtcNow.AddMinutes(-10), 100, 20));

        // 120 >= 100: entra. Si no se sumaran, cada fila serian 40 y no llegaria nunca.
        abusivas.ShouldHaveSingleItem().Rechazos.ShouldBe(120);
    }

    [Fact]
    public async Task Quien_no_llega_al_umbral_no_aparece()
    {
        await SembrarAsync("203.0.113.2", 50, 1);

        var abusivas = await WithUnitOfWorkAsync(
            () => _fuente.GetAsync(DateTime.UtcNow.AddMinutes(-10), 100, 20));

        abusivas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lo_de_FUERA_de_la_ventana_no_cuenta()
    {
        // El grueso es viejo: quien se paso hace una hora y ya paro no debe ser bloqueado
        // ahora. La ventana es lo que distingue "esta atacando" de "ataco una vez".
        await SembrarAsync("203.0.113.3", 500, 120);
        await SembrarAsync("203.0.113.3", 10, 1);

        var abusivas = await WithUnitOfWorkAsync(
            () => _fuente.GetAsync(DateTime.UtcNow.AddMinutes(-10), 100, 20));

        abusivas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Las_peores_primero_y_con_tope()
    {
        await SembrarAsync("203.0.113.10", 150, 1);
        await SembrarAsync("203.0.113.11", 900, 1);
        await SembrarAsync("203.0.113.12", 400, 1);

        var abusivas = await WithUnitOfWorkAsync(
            () => _fuente.GetAsync(DateTime.UtcNow.AddMinutes(-10), 100, 2));

        // El tope existe para que un ataque distribuido no llene la tabla de una pasada;
        // el orden, para que si hay tope se atienda primero a quien mas molesta.
        abusivas.Count.ShouldBe(2);
        abusivas[0].IpAddress.ShouldBe("203.0.113.11");
        abusivas[1].IpAddress.ShouldBe("203.0.113.12");
    }

    [Fact]
    public async Task Cada_IP_va_por_su_cuenta()
    {
        // Dos direcciones que por separado no llegan no pueden sumarse entre ellas.
        await SembrarAsync("203.0.113.20", 60, 1);
        await SembrarAsync("203.0.113.21", 60, 1);

        var abusivas = await WithUnitOfWorkAsync(
            () => _fuente.GetAsync(DateTime.UtcNow.AddMinutes(-10), 100, 20));

        abusivas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_bloqueo_automatico_se_distingue_de_uno_manual()
    {
        var repositorio = GetRequiredService<IBlockedIpRepository>();

        await WithUnitOfWorkAsync(() => repositorio.InsertAsync(
            new BlockedIp(_guids.Create(), "203.0.113.30", "Automático: 500 en 10 min",
                DateTime.UtcNow.AddHours(24), isAutomatic: true),
            autoSave: true));

        var guardado = await WithUnitOfWorkAsync(() => repositorio.FindByIpAsync("203.0.113.30"));

        guardado!.IsAutomatic.ShouldBeTrue();
        // Siempre con fin: un veto automatico indefinido convierte un umbral mal puesto en
        // un problema permanente que nadie recuerda haber creado.
        guardado.ExpiresUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Un_bloqueo_manual_NO_se_marca_como_automatico()
    {
        var repositorio = GetRequiredService<IBlockedIpRepository>();

        await WithUnitOfWorkAsync(() => repositorio.InsertAsync(
            new BlockedIp(_guids.Create(), "203.0.113.31", "A mano", null),
            autoSave: true));

        (await WithUnitOfWorkAsync(() => repositorio.FindByIpAsync("203.0.113.31")))!
            .IsAutomatic.ShouldBeFalse();
    }
}
