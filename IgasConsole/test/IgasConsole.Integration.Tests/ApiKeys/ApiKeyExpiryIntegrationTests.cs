using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.ApiKeys;
using IgasConsole.Notifications;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Caducidad de claves de API.
///
/// <para>El worker vive en el Host, que estos tests no cargan, así que lo que se prueba aquí
/// es la consulta que lo alimenta y el freno que impide que repita el aviso. Si la consulta
/// se equivoca, o avisa de claves que no tocan o deja caducar en silencio justo las que
/// importan — y ninguna de las dos cosas se nota mirando la pantalla.</para>
/// </summary>
public class ApiKeyExpiryIntegrationTests : IntegrationTestBase
{
    private readonly IApiKeyRepository _claves;
    private readonly INotificacionRepository _avisos;
    private readonly IGuidGenerator _guids;

    private static readonly Guid Ana = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid Beto = Guid.Parse("22222222-0000-0000-0000-000000000002");

    public ApiKeyExpiryIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _claves = GetRequiredService<IApiKeyRepository>();
        _avisos = GetRequiredService<INotificacionRepository>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private Task<ApiKey> CrearClaveAsync(
        Guid userId, string nombre, double? caducaEnDias, bool revocada = false)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var clave = new ApiKey(
                _guids.Create(), userId, null, nombre,
                _guids.Create().ToString("N") + _guids.Create().ToString("N"),
                "tpl_prueba12",
                caducaEnDias.HasValue ? DateTime.UtcNow.AddDays(caducaEnDias.Value) : null);

            if (revocada)
            {
                clave.Revocar(DateTime.UtcNow);
            }

            return await _claves.InsertAsync(clave, autoSave: true);
        });
    }

    private Task<System.Collections.Generic.List<ApiKey>> PorCaducarAsync(int dias = 7)
    {
        var ahora = DateTime.UtcNow;
        return WithUnitOfWorkAsync(
            () => _claves.GetExpiringAsync(ahora, ahora.AddDays(dias)));
    }

    [Fact]
    public async Task Solo_entran_las_que_caducan_dentro_de_la_ventana()
    {
        await CrearClaveAsync(Ana, "Caduca pronto", caducaEnDias: 3);
        await CrearClaveAsync(Ana, "Caduca tarde", caducaEnDias: 60);
        await CrearClaveAsync(Ana, "Sin caducidad", caducaEnDias: null);

        var porCaducar = await PorCaducarAsync();

        porCaducar.ShouldHaveSingleItem().Name.ShouldBe("Caduca pronto");
    }

    [Fact]
    public async Task Una_clave_YA_caducada_no_vuelve_a_avisar()
    {
        // Avisar de algo que ya pasó no evita nada, y llenaría la campana en cada pasada
        // del vigilante — para siempre, porque la fila se conserva.
        await CrearClaveAsync(Ana, "Caducada hace tiempo", caducaEnDias: -10);

        (await PorCaducarAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_clave_revocada_no_avisa()
    {
        // Ya no sirve: avisar de su caducidad seria pedirle a alguien que renueve algo que
        // el mismo desactivo.
        await CrearClaveAsync(Ana, "Revocada", caducaEnDias: 2, revocada: true);

        (await PorCaducarAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Salen_ordenadas_por_lo_que_caduca_antes()
    {
        await CrearClaveAsync(Ana, "En 6 dias", caducaEnDias: 6);
        await CrearClaveAsync(Ana, "En 1 dia", caducaEnDias: 1);
        await CrearClaveAsync(Ana, "En 3 dias", caducaEnDias: 3);

        var porCaducar = await PorCaducarAsync();

        porCaducar.Select(k => k.Name).ShouldBe(["En 1 dia", "En 3 dias", "En 6 dias"]);
    }

    [Fact]
    public async Task El_freno_de_repeticion_distingue_entre_personas()
    {
        // EL TEST QUE IMPORTA. El freno compara por titulo, y el titulo lleva el nombre de
        // la clave. Dos personas pueden llamar igual a la suya —"produccion" es el nombre
        // mas probable del mundo—: si el freno no mirara TAMBIEN el usuario, la primera
        // recibiria su aviso y la segunda se quedaria sin el suyo sin que nada fallara.
        var titulo = "Tu clave de API «produccion» caduca en 3 días";
        var desde = DateTime.UtcNow.AddDays(-2);

        await WithUnitOfWorkAsync(() => _avisos.InsertAsync(new Notificacion(
            _guids.Create(), null, Ana, null, NotificationSeverity.Warning,
            titulo, "cuerpo", "/api-keys", DateTime.UtcNow)));

        (await WithUnitOfWorkAsync(
            () => _avisos.ExistsRecentAsync(titulo, Ana, desde))).ShouldBeTrue();

        // Beto no ha sido avisado: su aviso tiene que salir.
        (await WithUnitOfWorkAsync(
            () => _avisos.ExistsRecentAsync(titulo, Beto, desde))).ShouldBeFalse();
    }

    [Fact]
    public async Task Un_aviso_viejo_deja_de_frenar()
    {
        // Asi llega un segundo recordatorio dentro de la ventana de aviso en vez de uno
        // solo, que es facil que pase desapercibido.
        var titulo = "Tu clave de API «x» caduca en 5 días";

        await WithUnitOfWorkAsync(() => _avisos.InsertAsync(new Notificacion(
            _guids.Create(), null, Ana, null, NotificationSeverity.Warning,
            titulo, "cuerpo", "/api-keys", DateTime.UtcNow.AddDays(-5))));

        (await WithUnitOfWorkAsync(() => _avisos.ExistsRecentAsync(
            titulo, Ana, DateTime.UtcNow.AddDays(-ApiKeyConsts.AvisoCaducidadSilencioDias))))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task El_aviso_de_caducidad_es_PERSONAL_no_de_administracion()
    {
        // Una clave es de quien la creo. Avisar a todos los administradores seria ruido y,
        // ademas, contarles que integraciones tiene montadas cada uno.
        var titulo = "Tu clave de API «privada» caduca en 2 días";

        await WithUnitOfWorkAsync(() => _avisos.InsertAsync(new Notificacion(
            _guids.Create(), null, Ana, null, NotificationSeverity.Warning,
            titulo, "cuerpo", "/api-keys", DateTime.UtcNow)));

        // Beto tiene TODOS los permisos y aun asi no lo ve.
        var deBeto = await WithUnitOfWorkAsync(() => _avisos.GetForUserAsync(
            Beto, ["IgasConsole.ApiKeys", "IgasConsole.Jobs"], false, 50));

        deBeto.ShouldBeEmpty();

        (await WithUnitOfWorkAsync(
            () => _avisos.GetForUserAsync(Ana, Array.Empty<string>(), false, 50)))
            .ShouldHaveSingleItem().Notificacion.Title.ShouldBe(titulo);
    }
}
