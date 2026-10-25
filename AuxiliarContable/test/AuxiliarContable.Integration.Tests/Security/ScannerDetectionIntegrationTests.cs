using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Security;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.AuditLogging;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Detección de IPs que sondean rutas.
///
/// <para>La señal vieja —rechazos del rate limiter— no bloqueaba a nadie en producción: los
/// escaneadores medidos iban a 378 y 591 peticiones por minuto con la cuota en 600, así que
/// nunca eran rechazados ni contados. Esta segunda señal mira el COMPORTAMIENTO.</para>
///
/// <para>Los números de estos tests no son inventados: salen del tráfico real del servidor.
/// Los escaneadores fallaban entre el 92 % y el 100 % de las veces; el cliente legítimo más
/// activo, el 1 %. Lo que se prueba es que el umbral cae limpiamente entre esos dos mundos —
/// porque el fallo caro aquí no es dejar pasar a un escaneador, es <b>bloquear a un usuario
/// de verdad</b>.</para>
/// </summary>
public class ScannerDetectionIntegrationTests : IntegrationTestBase
{
    private readonly IAbusiveIpSource _fuente;
    private readonly IAuditingStore _almacen;

    public ScannerDetectionIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _fuente = GetRequiredService<IAbusiveIpSource>();
        _almacen = GetRequiredService<IAuditingStore>();
    }

    /// <summary>Escribe peticiones como las escribe ABP: una entrada por petición.</summary>
    private async Task SembrarAsync(string ip, int correctas, int fallidas, int haceMinutos = 1)
    {
        var cuando = DateTime.UtcNow.AddMinutes(-haceMinutos);

        for (var i = 0; i < correctas; i++)
        {
            await GuardarAsync(ip, "/api/app/product", 200, cuando);
        }

        // Cada fallo va a una ruta DISTINTA, que es justo lo que hace un escaneador.
        for (var i = 0; i < fallidas; i++)
        {
            await GuardarAsync(ip, $"/.env.{i}", 404, cuando);
        }
    }

    private Task GuardarAsync(string ip, string url, int codigo, DateTime cuando)
    {
        return WithUnitOfWorkAsync(() => _almacen.SaveAsync(new AuditLogInfo
        {
            ApplicationName = "Test",
            ExecutionTime = cuando,
            ClientIpAddress = ip,
            Url = url,
            HttpStatusCode = codigo,
            HttpMethod = "GET"
        }));
    }

    private Task<System.Collections.Generic.List<IpEscaneando>> BuscarAsync(
        int minimoFallos = 40, int minimoPorcentaje = 80)
    {
        return WithUnitOfWorkAsync(() => _fuente.GetScannersAsync(
            DateTime.UtcNow.AddMinutes(-10), minimoFallos, minimoPorcentaje, 20));
    }

    [Fact]
    public async Task Un_escaneador_real_se_detecta()
    {
        // Perfil de 213.209.159.175 tal cual se midio: 332 peticiones, 331 fallidas, 100 %.
        // Se usa una escala menor para no sembrar cientos de filas por test.
        await SembrarAsync("203.0.113.10", correctas: 1, fallidas: 60);

        var encontrados = await BuscarAsync();

        var ip = encontrados.ShouldHaveSingleItem();
        ip.IpAddress.ShouldBe("203.0.113.10");
        ip.Fallos.ShouldBe(60);
        ip.PorcentajeFallo.ShouldBeGreaterThanOrEqualTo(80);
    }

    [Fact]
    public async Task UN_USUARIO_DE_VERDAD_NO_SE_BLOQUEA_NUNCA()
    {
        // EL TEST QUE IMPORTA. Perfil de 187.190.206.121, medido en produccion: 170
        // peticiones y solo 1 fallo. Bloquear a este seria el fallo caro — un escaneador
        // que se escapa vuelve mañana, pero una persona bloqueada no puede trabajar.
        await SembrarAsync("203.0.113.20", correctas: 169, fallidas: 1);

        (await BuscarAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_cliente_MUY_activo_con_bastantes_fallos_tampoco_se_bloquea()
    {
        // 500 peticiones y 50 fallos: supera el umbral de FALLOS pero solo falla el 10 %.
        // Sin la condicion del porcentaje, este cliente legitimo acabaria vetado por
        // acumular 404 sueltos a base de usar mucho la aplicacion.
        await SembrarAsync("203.0.113.30", correctas: 450, fallidas: 50);

        (await BuscarAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Fallar_SIEMPRE_pero_poco_tampoco_basta()
    {
        // 10 de 10 fallidas: el 100 %, pero son diez. Puede ser un enlace roto en un correo
        // viejo. Sin la condicion del recuento, se bloquearia por diez peticiones.
        await SembrarAsync("203.0.113.40", correctas: 0, fallidas: 10);

        (await BuscarAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Lo_de_fuera_de_la_ventana_no_cuenta()
    {
        // Un escaneador de ayer no justifica bloquear hoy: la ventana es deslizante.
        await SembrarAsync("203.0.113.50", correctas: 0, fallidas: 60, haceMinutos: 120);

        (await BuscarAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Se_devuelven_los_peores_primero()
    {
        await SembrarAsync("203.0.113.60", correctas: 0, fallidas: 45);
        await SembrarAsync("203.0.113.61", correctas: 0, fallidas: 90);

        var encontrados = await BuscarAsync();

        // Con el tope por pasada, el orden decide a quien se bloquea antes.
        encontrados.First().IpAddress.ShouldBe("203.0.113.61");
    }

    [Fact]
    public async Task Se_cuentan_las_rutas_distintas_que_probo()
    {
        // El recuento de rutas va en el motivo del bloqueo: es lo que permite decidir,
        // meses despues, si aquel veto tenia sentido.
        await SembrarAsync("203.0.113.70", correctas: 0, fallidas: 50);

        var ip = (await BuscarAsync()).ShouldHaveSingleItem();

        ip.RutasDistintas.ShouldBe(50);
    }

    [Fact]
    public async Task El_tope_de_resultados_se_respeta()
    {
        // Ante un ataque distribuido, sin tope una sola pasada llenaria la tabla y el panel
        // dejaria de ser legible justo cuando mas falta hace.
        for (var i = 0; i < 4; i++)
        {
            await SembrarAsync($"203.0.113.8{i}", correctas: 0, fallidas: 45);
        }

        var encontrados = await WithUnitOfWorkAsync(() => _fuente.GetScannersAsync(
            DateTime.UtcNow.AddMinutes(-10), 40, 80, maxResultados: 2));

        encontrados.Count.ShouldBe(2);
    }
}
