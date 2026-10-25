using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Sessions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Xunit;

namespace IgasConsole;

/// <summary>
/// Sesiones activas contra PostgreSQL real.
/// <para>Lo que se prueba son las reglas que hacen que "cerrar sesión en otro dispositivo"
/// signifique algo: solo ves las tuyas, solo cierras las tuyas, y "cerrar las demás" no te
/// echa a ti.</para>
/// </summary>
public class SessionsIntegrationTests : IntegrationTestBase
{
    private readonly IUserSessionAppService _sesiones;
    private readonly IIdentitySessionRepository _repositorio;
    private readonly IGuidGenerator _guids;

    /// <summary>El mismo id que finge FakeCurrentPrincipalAccessor.</summary>
    private static readonly Guid UsuarioActual = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    public SessionsIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _sesiones = GetRequiredService<IUserSessionAppService>();
        _repositorio = GetRequiredService<IIdentitySessionRepository>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private Task CrearAsync(string sessionId, Guid userId, string dispositivo = "Chrome", int haceMinutos = 0)
    {
        var cuando = DateTime.UtcNow.AddMinutes(-haceMinutos);

        return WithUnitOfWorkAsync(() => _repositorio.InsertAsync(
            new IdentitySession(
                _guids.Create(), sessionId, "web", dispositivo, userId, null,
                "web", "10.0.0.1", cuando, cuando),
            autoSave: true));
    }

    [Fact]
    public async Task Solo_se_ven_las_sesiones_propias()
    {
        await CrearAsync("mia-1", UsuarioActual);
        await CrearAsync("de-otro", Guid.NewGuid());

        var lista = await _sesiones.GetListAsync();

        // Si se colaran las de otro, esta pantalla seria un mapa de quien esta conectado
        // en toda la instalacion.
        // El navegador va en DeviceInfo; Device guarda el tipo de cliente ("web"), igual
        // que hace RegistradorDeSesiones en produccion.
        lista.ShouldHaveSingleItem().DeviceInfo.ShouldBe("Chrome");
    }

    [Fact]
    public async Task No_se_puede_cerrar_la_sesion_de_otro()
    {
        await CrearAsync("de-otro", Guid.NewGuid());
        var ajena = (await WithUnitOfWorkAsync(() => _repositorio.GetListAsync())).Single();

        // Sin esta comprobacion, cualquiera con sesion podria echar a otro con solo
        // adivinar un id.
        var ex = await Should.ThrowAsync<UserFriendlyException>(() => _sesiones.RevokeAsync(ajena.Id));
        ex.Message.ShouldContain("no es tuya");

        (await WithUnitOfWorkAsync(() => _repositorio.FindAsync("de-otro"))).ShouldNotBeNull();
    }

    [Fact]
    public async Task Cerrar_una_sesion_la_borra()
    {
        await CrearAsync("mia-1", UsuarioActual);
        var mia = (await _sesiones.GetListAsync()).Single();

        await _sesiones.RevokeAsync(mia.Id);

        // Borrada de verdad: el middleware valida por existencia, asi que una fila que se
        // quedara ahi seguiria dando acceso.
        (await WithUnitOfWorkAsync(() => _repositorio.FindAsync("mia-1"))).ShouldBeNull();
    }

    [Fact]
    public async Task Cerrar_una_sesion_que_ya_no_esta_no_es_un_error()
    {
        // Dos pestañas cerrando la misma sesion: la segunda no debe reventar.
        await Should.NotThrowAsync(() => _sesiones.RevokeAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task La_lista_marca_cual_es_la_sesion_actual()
    {
        // FakeCurrentPrincipalAccessor no pone claim de sesion, asi que ninguna es la
        // actual. Lo que importa aqui es que NO marque una al azar: si lo hiciera, el boton
        // "salir aqui" echaria de una sesion equivocada.
        await CrearAsync("una", UsuarioActual);
        await CrearAsync("otra", UsuarioActual);

        var lista = await _sesiones.GetListAsync();

        lista.Count.ShouldBe(2);
        lista.ShouldAllBe(s => !s.IsCurrent);
    }

    [Fact]
    public async Task Cerrar_las_demas_sin_saber_cual_es_la_propia_NO_las_borra_todas()
    {
        // EL TEST QUE IMPORTA. Sin identificar la sesion propia, "cerrar las demas" no
        // puede excluirla: borrarlas todas echaria tambien a quien acaba de pulsar el
        // boton, que es justo lo contrario de lo que pidio.
        await CrearAsync("una", UsuarioActual);
        await CrearAsync("otra", UsuarioActual);

        await Should.ThrowAsync<UserFriendlyException>(() => _sesiones.RevokeOthersAsync());

        (await _sesiones.GetListAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task La_lista_ordena_por_actividad_reciente()
    {
        await CrearAsync("vieja", UsuarioActual, "Firefox", haceMinutos: 600);
        await CrearAsync("reciente", UsuarioActual, "Chrome", haceMinutos: 2);

        var lista = await _sesiones.GetListAsync();

        // La que mas interesa revisar es la que lleva mas tiempo abierta sin reconocerse,
        // pero lo primero que uno busca es "cual soy yo": por eso manda la actividad.
        lista.First().DeviceInfo.ShouldBe("Chrome");
    }
}
