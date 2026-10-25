using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.Notifications;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Avisos dentro de la aplicación, contra PostgreSQL real.
///
/// <para>Lo que se prueba es la regla que decide QUIÉN VE QUÉ, porque es la única que puede
/// fallar sin dar la cara: un aviso de administración que se cuela a quien no debería
/// —o al revés, uno que no llega a nadie— se ve exactamente igual desde fuera que uno que
/// funciona.</para>
/// </summary>
public class NotificationsIntegrationTests : IntegrationTestBase
{
    private readonly INotificacionRepository _repositorio;
    private readonly IGuidGenerator _guids;

    private static readonly Guid Ana = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Beto = Guid.Parse("11111111-0000-0000-0000-000000000002");

    private const string PermisoJobs = "AuxiliarContable.Jobs";
    private const string PermisoSeguridad = "AuxiliarContable.Security.BlockedIps";

    public NotificationsIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _repositorio = GetRequiredService<INotificacionRepository>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private Task<Guid> CrearAsync(
        Guid? userId = null,
        string? permiso = null,
        string titulo = "Un aviso",
        NotificationSeverity severidad = NotificationSeverity.Info,
        int haceMinutos = 0)
    {
        var id = _guids.Create();

        return WithUnitOfWorkAsync(async () =>
        {
            await _repositorio.InsertAsync(new Notificacion(
                id, null, userId, permiso, severidad, titulo, "cuerpo", "/jobs",
                DateTime.UtcNow.AddMinutes(-haceMinutos)));
            return id;
        });
    }

    private Task<List<NotificacionConEstado>> VerAsync(
        Guid usuario, bool soloNoLeidas = false, params string[] permisos)
    {
        return WithUnitOfWorkAsync(
            () => _repositorio.GetForUserAsync(usuario, permisos, soloNoLeidas, 50));
    }

    [Fact]
    public async Task Un_aviso_personal_solo_lo_ve_su_destinatario()
    {
        await CrearAsync(userId: Ana, titulo: "Para Ana");

        (await VerAsync(Ana)).ShouldHaveSingleItem().Notificacion.Title.ShouldBe("Para Ana");
        (await VerAsync(Beto)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_aviso_por_permiso_lo_ve_quien_tiene_ESE_permiso()
    {
        await CrearAsync(permiso: PermisoJobs, titulo: "Trabajos abandonados");

        (await VerAsync(Ana, false, PermisoJobs))
            .ShouldHaveSingleItem().Notificacion.Title.ShouldBe("Trabajos abandonados");

        // Tiene permisos, pero no ESE.
        (await VerAsync(Beto, false, PermisoSeguridad)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sin_permisos_no_se_ve_NINGUN_aviso_de_administracion()
    {
        // EL TEST QUE IMPORTA. La consulta filtra con un Contains sobre la lista de
        // permisos; si esa lista vacia se tratara como "no filtrar" —que es como se
        // escriben muchos filtros opcionales— cualquier usuario recien creado veria de
        // golpe todos los avisos de la instalacion.
        await CrearAsync(permiso: PermisoJobs);
        await CrearAsync(permiso: PermisoSeguridad);

        (await VerAsync(Beto)).ShouldBeEmpty();
        (await WithUnitOfWorkAsync(
            () => _repositorio.CountUnreadAsync(Beto, Array.Empty<string>()))).ShouldBe(0);
    }

    [Fact]
    public async Task Se_ven_juntos_los_personales_y_los_del_permiso()
    {
        await CrearAsync(userId: Ana, titulo: "Personal", haceMinutos: 5);
        await CrearAsync(permiso: PermisoJobs, titulo: "De administracion", haceMinutos: 1);

        var lista = await VerAsync(Ana, false, PermisoJobs);

        // Y por fecha descendente: lo ultimo que paso es lo primero que se lee.
        lista.Select(f => f.Notificacion.Title)
            .ShouldBe(["De administracion", "Personal"]);
    }

    [Fact]
    public async Task Leer_es_de_cada_uno_no_del_aviso()
    {
        // EL OTRO TEST QUE IMPORTA. Un aviso por permiso lo ven varias personas. Si "leido"
        // fuera una columna del aviso, en cuanto lo abriera la primera desapareceria el
        // numero rojo de todas las demas.
        var id = await CrearAsync(permiso: PermisoJobs);

        await WithUnitOfWorkAsync(() => _repositorio.MarkReadAsync(Ana, [id]));

        (await VerAsync(Ana, false, PermisoJobs)).ShouldHaveSingleItem().Leida.ShouldBeTrue();
        (await VerAsync(Beto, false, PermisoJobs)).ShouldHaveSingleItem().Leida.ShouldBeFalse();

        (await WithUnitOfWorkAsync(
            () => _repositorio.CountUnreadAsync(Ana, [PermisoJobs]))).ShouldBe(0);
        (await WithUnitOfWorkAsync(
            () => _repositorio.CountUnreadAsync(Beto, [PermisoJobs]))).ShouldBe(1);
    }

    [Fact]
    public async Task Marcar_dos_veces_no_duplica_ni_mueve_la_fecha()
    {
        var id = await CrearAsync(userId: Ana);

        await WithUnitOfWorkAsync(() => _repositorio.MarkReadAsync(Ana, [id]));
        var primera = (await VerAsync(Ana)).Single();

        await WithUnitOfWorkAsync(() => _repositorio.MarkReadAsync(Ana, [id]));

        // Sigue siendo un solo aviso leido: la columna es UNICA y una segunda fila haria
        // reventar la insercion, ademas de mentir sobre cuando se vio de verdad.
        var despues = await VerAsync(Ana);
        despues.ShouldHaveSingleItem().Leida.ShouldBeTrue();
        primera.Leida.ShouldBeTrue();
    }

    [Fact]
    public async Task El_filtro_de_sin_leer_deja_fuera_lo_ya_leido()
    {
        var leido = await CrearAsync(userId: Ana, titulo: "Ya visto");
        await CrearAsync(userId: Ana, titulo: "Nuevo");

        await WithUnitOfWorkAsync(() => _repositorio.MarkReadAsync(Ana, [leido]));

        (await VerAsync(Ana, soloNoLeidas: true))
            .ShouldHaveSingleItem().Notificacion.Title.ShouldBe("Nuevo");
    }

    [Fact]
    public async Task Marcar_todo_solo_alcanza_lo_que_esa_persona_ve()
    {
        await CrearAsync(userId: Ana);
        await CrearAsync(permiso: PermisoJobs);
        await CrearAsync(permiso: PermisoSeguridad, titulo: "De otro ambito");

        await WithUnitOfWorkAsync(() => _repositorio.MarkAllReadAsync(Ana, [PermisoJobs]));

        // El de seguridad sigue sin leer PARA QUIEN LO VE: "marcar todo" no puede dar por
        // atendido lo que uno ni siquiera tiene delante.
        (await WithUnitOfWorkAsync(
            () => _repositorio.CountUnreadAsync(Ana, [PermisoJobs]))).ShouldBe(0);
        (await WithUnitOfWorkAsync(
            () => _repositorio.CountUnreadAsync(Beto, [PermisoSeguridad]))).ShouldBe(1);
    }

    [Fact]
    public async Task Un_aviso_sin_destinatario_o_con_los_dos_se_rechaza()
    {
        // Sin destinatario no lo veria nadie; con los dos no estaria claro si el permiso
        // restringe o amplia. Las dos formas de equivocarse producen un aviso que parece
        // enviado y no lo esta — por eso se rompe aqui y no en silencio.
        Should.Throw<AbpException>(() => new Notificacion(
            Guid.NewGuid(), null, null, null,
            NotificationSeverity.Info, "t", "c", null, DateTime.UtcNow));

        Should.Throw<AbpException>(() => new Notificacion(
            Guid.NewGuid(), null, Ana, PermisoJobs,
            NotificationSeverity.Info, "t", "c", null, DateTime.UtcNow));
    }

    [Fact]
    public void Una_fecha_que_no_es_UTC_se_rechaza_al_construirlo()
    {
        Should.Throw<AbpException>(() => new Notificacion(
            Guid.NewGuid(), null, Ana, null, NotificationSeverity.Info, "t", "c", null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public async Task Un_cuerpo_larguisimo_se_recorta_en_vez_de_reventar()
    {
        // El aviso nace casi siempre porque algo va mal, y puede llevar dentro el mensaje
        // de una excepcion. Reventar aqui convertiria "algo fallo" en "algo fallo y ademas
        // no se aviso".
        var enorme = new string('x', NotificationConsts.MaxBodyLength * 3);

        var id = _guids.Create();
        await WithUnitOfWorkAsync(() => _repositorio.InsertAsync(new Notificacion(
            id, null, Ana, null, NotificationSeverity.Error, "t", enorme, null,
            DateTime.UtcNow)));

        (await VerAsync(Ana)).ShouldHaveSingleItem()
            .Notificacion.Body.Length.ShouldBe(NotificationConsts.MaxBodyLength);
    }

    [Fact]
    public async Task El_freno_anti_repeticion_reconoce_un_aviso_igual_y_reciente()
    {
        // Es lo que impide que el vigilante de trabajos abandonados avise cada hora del
        // mismo problema hasta que el aviso deje de leerse.
        await CrearAsync(permiso: PermisoJobs, titulo: "3 trabajos se han abandonado");

        var desde = DateTime.UtcNow.AddHours(-24);

        (await WithUnitOfWorkAsync(
            () => _repositorio.ExistsRecentAsync("3 trabajos se han abandonado", null, desde)))
            .ShouldBeTrue();

        // Si el numero cambia es OTRO problema, y vuelve a avisar.
        (await WithUnitOfWorkAsync(
            () => _repositorio.ExistsRecentAsync("5 trabajos se han abandonado", null, desde)))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Un_aviso_igual_pero_viejo_ya_no_frena()
    {
        await CrearAsync(
            permiso: PermisoJobs, titulo: "3 trabajos se han abandonado",
            haceMinutos: 60 * 30);

        (await WithUnitOfWorkAsync(() => _repositorio.ExistsRecentAsync(
            "3 trabajos se han abandonado", null, DateTime.UtcNow.AddHours(-24))))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task La_purga_se_lleva_los_avisos_viejos_y_sus_marcas_de_lectura()
    {
        var viejo = await CrearAsync(userId: Ana, titulo: "Viejo", haceMinutos: 60 * 24 * 40);
        await CrearAsync(userId: Ana, titulo: "Reciente");

        await WithUnitOfWorkAsync(() => _repositorio.MarkReadAsync(Ana, [viejo]));

        var corte = DateTime.UtcNow.AddDays(-NotificationConsts.RetentionDays);
        var borrados = await WithUnitOfWorkAsync(() => _repositorio.PurgeOlderThanAsync(corte));

        borrados.ShouldBe(1);
        (await VerAsync(Ana)).ShouldHaveSingleItem().Notificacion.Title.ShouldBe("Reciente");
    }

    [Fact]
    public async Task Los_permisos_destinatario_salen_sin_repetirse()
    {
        // Es la lista por la que se le pregunta a ABP en cada carga: si trajera duplicados,
        // se comprobaria el mismo permiso varias veces por peticion.
        await CrearAsync(permiso: PermisoJobs);
        await CrearAsync(permiso: PermisoJobs);
        await CrearAsync(permiso: PermisoSeguridad);
        await CrearAsync(userId: Ana);

        var permisos = await WithUnitOfWorkAsync(() => _repositorio.GetTargetPermissionsAsync());

        permisos.OrderBy(p => p, StringComparer.Ordinal)
            .ShouldBe([PermisoJobs, PermisoSeguridad]);
    }
}
