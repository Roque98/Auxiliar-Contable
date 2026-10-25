using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Security;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Lista de IPs bloqueadas contra PostgreSQL real.
/// <para>Lo que se prueba no es el CRUD, sino las tres reglas que hacen que esto sea usable
/// sin pegarse un tiro en el pie: no puedes bloquearte a ti mismo, no se guardan cadenas que
/// no son IPs, y un bloqueo con plazo deja de aplicar cuando vence.</para>
/// </summary>
public class BlockedIpsIntegrationTests : IntegrationTestBase
{
    private readonly IBlockedIpAppService _bloqueos;
    private readonly IBlockedIpRepository _repositorio;
    private readonly CallerIpProviderDePrueba _ipDelLlamante;

    public BlockedIpsIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _bloqueos = GetRequiredService<IBlockedIpAppService>();
        _repositorio = GetRequiredService<IBlockedIpRepository>();
        _ipDelLlamante = (CallerIpProviderDePrueba)GetRequiredService<ICallerIpProvider>();
        _ipDelLlamante.Ip = "10.0.0.1";
    }

    [Fact]
    public async Task Bloquear_y_listar()
    {
        await _bloqueos.BlockAsync(new BlockIpInput { IpAddress = "203.0.113.5", Reason = "Escaneo" });

        var lista = await _bloqueos.GetListAsync();

        var fila = lista.ShouldHaveSingleItem();
        fila.IpAddress.ShouldBe("203.0.113.5");
        fila.Reason.ShouldBe("Escaneo");
        fila.IsActive.ShouldBeTrue();
        fila.ExpiresUtc.ShouldBeNull(); // sin plazo = indefinido
    }

    [Fact]
    public async Task NO_puedes_bloquear_tu_propia_IP()
    {
        // EL TEST QUE IMPORTA. Este panel se administra desde la misma web que protege:
        // bloquear tu propia direccion te deja fuera de la herramienta con la que
        // desbloquearias, y la unica salida seria entrar a la base a mano.
        var ex = await Should.ThrowAsync<UserFriendlyException>(
            () => _bloqueos.BlockAsync(new BlockIpInput { IpAddress = "10.0.0.1", Reason = "Ups" }));

        ex.Message.ShouldContain("tu propia dirección");
        (await _bloqueos.GetListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cadena_que_no_es_una_IP_se_rechaza()
    {
        // Guardarla daria sensacion de proteccion sin protegar de nada: no coincidiria
        // nunca con ningun visitante.
        var ex = await Should.ThrowAsync<UserFriendlyException>(
            () => _bloqueos.BlockAsync(new BlockIpInput { IpAddress = "no-soy-una-ip", Reason = "x" }));

        ex.Message.ShouldContain("no es una dirección IP válida");
    }

    [Fact]
    public async Task No_se_puede_bloquear_dos_veces_la_misma()
    {
        await _bloqueos.BlockAsync(new BlockIpInput { IpAddress = "203.0.113.9", Reason = "Primera" });

        // Dos filas para la misma IP harian ambiguo cual plazo manda.
        await Should.ThrowAsync<UserFriendlyException>(
            () => _bloqueos.BlockAsync(new BlockIpInput { IpAddress = "203.0.113.9", Reason = "Segunda" }));
    }

    [Fact]
    public async Task Desbloquear_borra_de_verdad()
    {
        var creado = await _bloqueos.BlockAsync(
            new BlockIpInput { IpAddress = "203.0.113.7", Reason = "Prueba" });

        await _bloqueos.UnblockAsync(creado.Id);

        (await _bloqueos.GetListAsync()).ShouldBeEmpty();
        // Y no queda una fila oculta que el middleware tendria que aprender a ignorar.
        (await WithUnitOfWorkAsync(() => _repositorio.FindByIpAsync("203.0.113.7"))).ShouldBeNull();
    }

    [Fact]
    public async Task Un_bloqueo_con_plazo_guarda_su_caducidad()
    {
        var antes = DateTime.UtcNow;

        var creado = await _bloqueos.BlockAsync(
            new BlockIpInput { IpAddress = "203.0.113.11", Reason = "Temporal", Hours = 2 });

        creado.ExpiresUtc.ShouldNotBeNull();
        creado.ExpiresUtc!.Value.ShouldBeGreaterThan(antes.AddHours(1));
        creado.ExpiresUtc.Value.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Un_bloqueo_caducado_deja_de_aplicar_pero_sigue_a_la_vista()
    {
        // Nacido caducado, como el que se puso hace tres dias por 1 hora.
        await WithUnitOfWorkAsync(() => _repositorio.InsertAsync(
            new BlockedIp(Guid.NewGuid(), "203.0.113.13", "Ya vencido", DateTime.UtcNow.AddHours(-1)),
            autoSave: true));

        var vigentes = await WithUnitOfWorkAsync(() => _repositorio.GetVigentesAsync(DateTime.UtcNow));
        vigentes.ShouldBeEmpty(); // el middleware ya no lo carga

        // Pero en el panel sigue viendose, marcado como inactivo: saber que algo ESTUVO
        // bloqueado es informacion, y borrarlo solo porque vencio la esconde.
        var lista = await _bloqueos.GetListAsync();
        lista.ShouldHaveSingleItem().IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task El_motivo_es_obligatorio()
    {
        // Dentro de tres meses, una lista de IPs sin motivo no se puede revisar: nadie se
        // atreve a quitar nada porque nadie sabe por que esta.
        await Should.ThrowAsync<Exception>(
            () => _bloqueos.BlockAsync(new BlockIpInput { IpAddress = "203.0.113.21", Reason = "" }));
    }
}
