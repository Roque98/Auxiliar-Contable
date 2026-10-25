using System.Threading.Tasks;
using IgasConsole.Monitoring;
using Shouldly;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Tests de la configuración de avisos. El que de verdad importa aquí es el primero:
/// guardar el token NO puede apagar los avisos, que es lo que pasaba en producción.
/// </summary>
public class MonitoringSettingsIntegrationTests : IntegrationTestBase
{
    private readonly IMonitoringSettingsAppService _servicio;

    public MonitoringSettingsIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _servicio = GetRequiredService<IMonitoringSettingsAppService>();
    }

    [Fact]
    public async Task Guardar_sin_mandar_el_interruptor_NO_apaga_los_avisos()
    {
        // Avisos encendidos.
        await WithUnitOfWorkAsync(() =>
            _servicio.UpdateAsync(new UpdateMonitoringSettingsDto { AlertsEnabled = true }));

        // Ahora se cambia SOLO el chat, sin mandar el interruptor: es lo que hace la
        // pantalla al guardar el token. Antes esto enviaba alertsEnabled=false con el
        // valor obsoleto de la pagina y apagaba los avisos en silencio.
        var despues = await WithUnitOfWorkAsync(() =>
            _servicio.UpdateAsync(new UpdateMonitoringSettingsDto
            {
                AlertsEnabled = null,
                ChatIds = "-100123456789"
            }));

        despues.AlertsEnabled.ShouldBeTrue();
        despues.ChatIds.ShouldBe("-100123456789");
    }

    [Fact]
    public async Task El_interruptor_se_puede_apagar_cuando_se_manda_explicitamente()
    {
        await WithUnitOfWorkAsync(() =>
            _servicio.UpdateAsync(new UpdateMonitoringSettingsDto { AlertsEnabled = true }));

        var despues = await WithUnitOfWorkAsync(() =>
            _servicio.UpdateAsync(new UpdateMonitoringSettingsDto { AlertsEnabled = false }));

        despues.AlertsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Un_token_vacio_conserva_el_que_ya_estaba_guardado()
    {
        await WithUnitOfWorkAsync(() =>
            _servicio.UpdateAsync(new UpdateMonitoringSettingsDto { BotToken = "123456:ABCDEF" }));

        var despues = await WithUnitOfWorkAsync(() =>
            _servicio.UpdateAsync(new UpdateMonitoringSettingsDto { BotToken = null }));

        despues.HasBotToken.ShouldBeTrue();
    }
}
