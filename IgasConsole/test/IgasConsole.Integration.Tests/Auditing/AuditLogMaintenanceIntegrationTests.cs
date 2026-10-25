using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Auditing;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.AuditLogging;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Tests de la lectura y purga del registro de peticiones. Lo que importa aquí es que la
/// purga BORRE de verdad (la auditoría de ABP no trae una) y que el filtro por código de
/// estado deje pasar solo lo que falló.
/// </summary>
public class AuditLogMaintenanceIntegrationTests : IntegrationTestBase
{
    private readonly IAuditLogMaintenanceRepository _repositorio;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IAuditLogInfoToAuditLogConverter _convertidor;

    public AuditLogMaintenanceIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _repositorio = GetRequiredService<IAuditLogMaintenanceRepository>();
        _auditLogRepository = GetRequiredService<IAuditLogRepository>();
        // El mismo convertidor que usa ABP para persistir la auditoría: el constructor de
        // AuditLog es posicional y con dos decenas de parámetros, y replicarlo a mano en
        // el test lo ataría a un orden que puede cambiar entre versiones.
        _convertidor = GetRequiredService<IAuditLogInfoToAuditLogConverter>();
    }

    private async Task InsertarAsync(
        DateTime cuandoUtc, string url, int estado,
        string ip = "1.2.3.4", string? usuario = null)
    {
        var info = new AuditLogInfo
        {
            ApplicationName = "Test",
            ExecutionTime = cuandoUtc,
            ExecutionDuration = 5,
            HttpMethod = "GET",
            Url = url,
            HttpStatusCode = estado,
            ClientIpAddress = ip,
            UserName = usuario
        };

        var entrada = await _convertidor.ConvertAsync(info);

        await WithUnitOfWorkAsync(() => _auditLogRepository.InsertAsync(entrada, autoSave: true));
    }

    [Fact]
    public async Task GetRecent_devuelve_las_peticiones_recientes_primero()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-10), "/api/vieja", 200);
        await InsertarAsync(ahora.AddMinutes(-1), "/api/nueva", 200);

        // WithUnitOfWorkAsync: el repositorio pide el DbContext del UoW ambiental, que en
        // la aplicación real pone ABP alrededor de cada application service.
        var entradas = await WithUnitOfWorkAsync(() =>
            _repositorio.GetRecentAsync(ahora.AddHours(-1), 50));

        entradas.Count.ShouldBe(2);
        entradas[0].Url.ShouldBe("/api/nueva");
    }

    [Fact]
    public async Task GetRecent_filtra_por_codigo_de_estado()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-3), "/api/ok", 200);
        await InsertarAsync(ahora.AddMinutes(-2), "/global-protect/login.esp", 404);

        var soloFallos = await WithUnitOfWorkAsync(() =>
            _repositorio.GetRecentAsync(ahora.AddHours(-1), 50, minStatusCode: 400));

        soloFallos.Count.ShouldBe(1);
        soloFallos[0].HttpStatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task GetRecent_filtra_por_IP_exacta_y_no_por_coincidencia_parcial()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-3), "/api/a", 200, ip: "1.2.3.4");
        // Contiene "1.2.3.4" como subcadena: si el filtro fuera parcial, esta colaría
        // y estaríamos atribuyendo a una IP peticiones que no hizo.
        await InsertarAsync(ahora.AddMinutes(-2), "/api/b", 200, ip: "11.2.3.45");

        var soloUna = await WithUnitOfWorkAsync(() =>
            _repositorio.GetRecentAsync(ahora.AddHours(-1), 50, ipAddress: "1.2.3.4"));

        soloUna.Count.ShouldBe(1);
        soloUna[0].Url.ShouldBe("/api/a");
    }

    [Fact]
    public async Task GetRecent_filtra_por_usuario_sin_distinguir_mayusculas()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-3), "/api/a", 200, usuario: "admin");
        await InsertarAsync(ahora.AddMinutes(-2), "/api/b", 200, usuario: "otro");

        var encontrado = await WithUnitOfWorkAsync(() =>
            _repositorio.GetRecentAsync(ahora.AddHours(-1), 50, userName: "ADM"));

        encontrado.Count.ShouldBe(1);
        encontrado[0].UserName.ShouldBe("admin");
    }

    [Fact]
    public async Task GetRecent_combina_los_filtros()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddMinutes(-4), "/api/ok", 200, ip: "9.9.9.9");
        await InsertarAsync(ahora.AddMinutes(-3), "/api/falla", 500, ip: "9.9.9.9");
        await InsertarAsync(ahora.AddMinutes(-2), "/api/falla", 500, ip: "8.8.8.8");

        var resultado = await WithUnitOfWorkAsync(() =>
            _repositorio.GetRecentAsync(
                ahora.AddHours(-1), 50, minStatusCode: 400, ipAddress: "9.9.9.9"));

        resultado.Count.ShouldBe(1);
        resultado[0].Url.ShouldBe("/api/falla");
        resultado[0].ClientIpAddress.ShouldBe("9.9.9.9");
    }

    [Fact]
    public async Task PurgeOlderThan_borra_de_verdad_las_entradas_viejas()
    {
        var ahora = DateTime.UtcNow;

        await InsertarAsync(ahora.AddDays(-40), "/api/antigua", 200);
        await InsertarAsync(ahora.AddMinutes(-1), "/api/reciente", 200);

        var borradas = await WithUnitOfWorkAsync(() =>
            _repositorio.PurgeOlderThanAsync(ahora.AddDays(-30)));

        borradas.ShouldBe(1);

        var quedan = await WithUnitOfWorkAsync(() =>
            _repositorio.GetRecentAsync(ahora.AddDays(-365), 50));
        quedan.Count.ShouldBe(1);
        quedan.Single().Url.ShouldBe("/api/reciente");
    }
}
