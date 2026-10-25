using System;
using System.Threading.Tasks;
using IgasConsole.MiCuenta;
using Shouldly;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.SecurityLog;
using Xunit;

namespace IgasConsole;

/// <summary>
/// El resumen de «Mi cuenta» contra PostgreSQL real.
///
/// <para>Lo que se prueba es lo que puede equivocarse <b>en silencio</b>: que los recuentos
/// sean los del usuario que mira y no los de la instalación entera, y que «acceso anterior»
/// signifique de verdad el anterior y no el de ahora mismo.</para>
/// </summary>
public class MiCuentaIntegrationTests : IntegrationTestBase
{
    private readonly IMiCuentaAppService _cuenta;
    private readonly IIdentitySessionRepository _sesiones;
    private readonly IIdentitySecurityLogRepository _registro;
    private readonly IIdentityUserRepository _usuarios;
    private readonly IGuidGenerator _guids;

    /// <summary>El mismo id que finge FakeCurrentPrincipalAccessor.</summary>
    private static readonly Guid UsuarioActual = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    public MiCuentaIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _cuenta = GetRequiredService<IMiCuentaAppService>();
        _sesiones = GetRequiredService<IIdentitySessionRepository>();
        _registro = GetRequiredService<IIdentitySecurityLogRepository>();
        _usuarios = GetRequiredService<IIdentityUserRepository>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    /// <summary>El usuario que finge el arnés no existe en la base: hay que crearlo.</summary>
    private Task CrearUsuarioActualAsync() =>
        WithUnitOfWorkAsync(() => _usuarios.InsertAsync(
            new IdentityUser(UsuarioActual, "quien-mira", "quien-mira@ejemplo.test"),
            autoSave: true));

    private Task SesionAsync(string sessionId, Guid userId)
    {
        var cuando = DateTime.UtcNow;
        return WithUnitOfWorkAsync(() => _sesiones.InsertAsync(
            new IdentitySession(
                _guids.Create(), sessionId, "web", "Chrome", userId, null,
                "web", "10.0.0.1", cuando, cuando),
            autoSave: true));
    }

    private Task AccesoAsync(Guid userId, DateTime cuando, string accion = "LoginSucceeded")
    {
        // El constructor copia `CreationTime` del propio `SecurityLogInfo`, así que el
        // instante se fija aquí y no hace falta reflexión — mismo patrón que
        // `SecurityLogIntegrationTests`.
        var fila = new IdentitySecurityLog(_guids, new SecurityLogInfo
        {
            Identity = "Identity",
            Action = accion,
            UserId = userId,
            ApplicationName = "Test",
            CreationTime = cuando,
        });

        return WithUnitOfWorkAsync(() => _registro.InsertAsync(fila, autoSave: true));
    }

    [Fact]
    public async Task Los_recuentos_son_los_del_usuario_que_mira_y_no_los_de_todos()
    {
        await CrearUsuarioActualAsync();
        await SesionAsync("mia-1", UsuarioActual);
        await SesionAsync("mia-2", UsuarioActual);
        await SesionAsync("de-otro", Guid.NewGuid());

        var resumen = await _cuenta.GetAsync();

        // Si se colaran las de otro, la portada de la cuenta sería un recuento de la
        // instalación disfrazado de dato personal.
        resumen.SesionesActivas.ShouldBe(2);
    }

    /// <summary>
    /// ⚠️ <b>El test que justifica el método.</b> Cuando esta pantalla se abre, la entrada
    /// del inicio de sesión en curso <b>ya está escrita</b>. Quedarse con la más reciente
    /// enseñaría «último acceso: hace unos segundos» — el propio reflejo de quien mira, que
    /// no responde a nada. Lo que se quiere saber es si la vez ANTERIOR fuiste tú.
    /// </summary>
    [Fact]
    public async Task Acceso_anterior_es_el_penultimo_y_no_el_de_ahora()
    {
        await CrearUsuarioActualAsync();

        var anteayer = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        var ahora = new DateTime(2026, 8, 26, 18, 0, 0, DateTimeKind.Utc);

        await AccesoAsync(UsuarioActual, anteayer);
        await AccesoAsync(UsuarioActual, ahora);

        var resumen = await _cuenta.GetAsync();

        resumen.AccesoAnteriorUtc.ShouldNotBeNull();
        resumen.AccesoAnteriorUtc!.Value.ShouldBe(anteayer, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task La_primera_vez_que_entras_no_hay_acceso_anterior()
    {
        await CrearUsuarioActualAsync();
        await AccesoAsync(UsuarioActual, DateTime.UtcNow);

        // Nulo, no la fecha de ahora: enseñar la sesión en curso como «anterior» sería
        // mentir, y esta pantalla la usa alguien que sospecha de un acceso ajeno.
        (await _cuenta.GetAsync()).AccesoAnteriorUtc.ShouldBeNull();
    }

    /// <summary>
    /// El registro de seguridad guarda muchas acciones —cambios de contraseña, bloqueos,
    /// intentos fallidos— y solo una contesta «¿cuándo entré antes?».
    /// </summary>
    [Fact]
    public async Task Un_intento_fallido_no_cuenta_como_acceso_anterior()
    {
        await CrearUsuarioActualAsync();

        var entrada = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        await AccesoAsync(UsuarioActual, entrada);
        await AccesoAsync(UsuarioActual, new DateTime(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc), "LoginFailed");
        await AccesoAsync(UsuarioActual, new DateTime(2026, 8, 26, 18, 0, 0, DateTimeKind.Utc));

        var resumen = await _cuenta.GetAsync();

        // Con el fallido contando, el «anterior» sería el intento de alguien que NO entró.
        resumen.AccesoAnteriorUtc!.Value.ShouldBe(entrada, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task El_acceso_anterior_de_otro_usuario_no_es_el_mio()
    {
        await CrearUsuarioActualAsync();
        await AccesoAsync(Guid.NewGuid(), new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc));
        await AccesoAsync(Guid.NewGuid(), new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc));

        (await _cuenta.GetAsync()).AccesoAnteriorUtc.ShouldBeNull();
    }
}
