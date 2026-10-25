using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Security;
using Shouldly;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.SecurityLog;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Registro de seguridad contra PostgreSQL real.
///
/// <para>La tabla la llena ABP sola; lo que se prueba aquí es la lectura: que los filtros
/// acoten de verdad, que el total corresponda a lo filtrado —y no al de todas las filas— y
/// que un intento fallido se distinga de uno correcto. Un panel de seguridad que cuenta mal
/// o mezcla éxitos con fallos es peor que no tenerlo: se mira y se saca la conclusión
/// contraria.</para>
/// </summary>
public class SecurityLogIntegrationTests : IntegrationTestBase
{
    private readonly ISecurityLogAppService _registro;
    private readonly IIdentitySecurityLogRepository _repositorio;
    private readonly IGuidGenerator _guids;

    public SecurityLogIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _registro = GetRequiredService<ISecurityLogAppService>();
        _repositorio = GetRequiredService<IIdentitySecurityLogRepository>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private Task SembrarAsync(
        string accion, string usuario = "admin", string ip = "10.0.0.1", int haceDias = 0)
    {
        // El constructor copia CreationTime del propio SecurityLogInfo, así que la ventana
        // de días se prueba dándole la fecha aquí — sin reflexión.
        var log = new IdentitySecurityLog(_guids, new SecurityLogInfo
        {
            Identity = "Identity",
            Action = accion,
            UserName = usuario,
            ClientIpAddress = ip,
            ApplicationName = "Test",
            CreationTime = DateTime.UtcNow.AddDays(-haceDias)
        });

        return WithUnitOfWorkAsync(() => _repositorio.InsertAsync(log, autoSave: true));
    }

    [Fact]
    public async Task Un_intento_fallido_se_distingue_de_uno_correcto()
    {
        // Es lo primero que se mira en esta pantalla. Si todo se pintara igual, habria que
        // leer el nombre interno que usa ABP en cada fila para saber que paso.
        await SembrarAsync("LoginSucceeded");
        await SembrarAsync("LoginFailed");

        var todas = await _registro.GetListAsync(new GetSecurityLogsInput { MaxResultCount = 50 });

        todas.Items.Single(x => x.Action == "LoginFailed").IsFailure.ShouldBeTrue();
        todas.Items.Single(x => x.Action == "LoginSucceeded").IsFailure.ShouldBeFalse();
    }

    [Fact]
    public async Task El_filtro_por_accion_acota_las_filas_Y_EL_TOTAL()
    {
        // EL TEST QUE IMPORTA. Si el total no siguiera al filtro, la pantalla diria
        // "1 de 4" y ese 4 no significaria nada — se leeria como "hay 4 fallos".
        await SembrarAsync("LoginSucceeded");
        await SembrarAsync("LoginSucceeded");
        await SembrarAsync("LoginSucceeded");
        await SembrarAsync("LoginFailed");

        var fallidos = await _registro.GetListAsync(
            new GetSecurityLogsInput { ActionName = "LoginFailed", MaxResultCount = 50 });

        fallidos.Items.Count.ShouldBe(1);
        fallidos.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task El_filtro_por_usuario_no_arrastra_a_los_demas()
    {
        await SembrarAsync("LoginFailed", usuario: "ana");
        await SembrarAsync("LoginFailed", usuario: "beto");

        var deAna = await _registro.GetListAsync(
            new GetSecurityLogsInput { UserName = "ana", MaxResultCount = 50 });

        deAna.Items.ShouldHaveSingleItem().UserName.ShouldBe("ana");
        deAna.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task El_filtro_por_IP_es_exacto()
    {
        // Exacto y no parcial: "10.0.0.1" aparece dentro de "110.0.0.15", y un panel de
        // seguridad que confunde direcciones lleva a bloquear a quien no era.
        await SembrarAsync("LoginFailed", ip: "10.0.0.1");
        await SembrarAsync("LoginFailed", ip: "110.0.0.15");

        var resultado = await _registro.GetListAsync(
            new GetSecurityLogsInput { ClientIpAddress = "10.0.0.1", MaxResultCount = 50 });

        resultado.Items.ShouldHaveSingleItem().ClientIpAddress.ShouldBe("10.0.0.1");
    }

    [Fact]
    public async Task Lo_anterior_a_la_ventana_de_dias_queda_fuera()
    {
        await SembrarAsync("LoginFailed", haceDias: 30);
        await SembrarAsync("LoginFailed");

        var semana = await _registro.GetListAsync(
            new GetSecurityLogsInput { Days = 7, MaxResultCount = 50 });

        semana.Items.Count.ShouldBe(1);
        semana.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Lo_mas_reciente_va_primero()
    {
        await SembrarAsync("LoginSucceeded", usuario: "viejo", haceDias: 3);
        await SembrarAsync("LoginFailed", usuario: "nuevo");

        var lista = await _registro.GetListAsync(new GetSecurityLogsInput { MaxResultCount = 50 });

        // Lo que se viene a mirar es lo ultimo que paso.
        lista.Items.First().UserName.ShouldBe("nuevo");
    }

    [Fact]
    public async Task Las_acciones_del_desplegable_salen_de_los_datos_sin_repetirse()
    {
        // Se calculan de la tabla y no de una lista fija: los nombres los pone el modulo
        // Identity de ABP y pueden cambiar al actualizarlo. Una lista fija se quedaria
        // desfasada en silencio y ofreceria filtros que no encuentran nada.
        await SembrarAsync("LoginSucceeded");
        await SembrarAsync("LoginSucceeded");
        await SembrarAsync("LoginFailed");

        var filtros = await _registro.GetFiltersAsync();

        filtros.Actions.ShouldBe(["LoginFailed", "LoginSucceeded"]);
    }
}
