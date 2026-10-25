using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Jobs;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Guids;
using Xunit;

namespace IgasConsole;

/// <summary>
/// El panel de trabajos en segundo plano, contra PostgreSQL real.
///
/// <para>Lo que se prueba aquí no es que ABP sepa reintentar —eso es suyo— sino las dos
/// piezas que se han añadido encima y que pueden mentir en silencio: el ESTADO, que no es
/// una columna sino una combinación de tres, y el HISTORIAL DE FALLOS, que es lo único que
/// dice por qué algo no funciona.</para>
///
/// <para>Los trabajos se insertan pero no se ejecutan (ver
/// <c>IgasConsolePostgreSqlTestModule</c>), así que las filas se pueden montar en cualquier
/// estado y quedarse quietas.</para>
/// </summary>
public class JobsPanelIntegrationTests : IntegrationTestBase
{
    private readonly IJobAppService _panel;
    private readonly IJobMaintenanceRepository _consulta;
    private readonly IJobFailureRepository _fallos;
    private readonly IBackgroundJobRepository _trabajos;
    private readonly IGuidGenerator _guids;

    public JobsPanelIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _panel = GetRequiredService<IJobAppService>();
        _consulta = GetRequiredService<IJobMaintenanceRepository>();
        _fallos = GetRequiredService<IJobFailureRepository>();
        _trabajos = GetRequiredService<IBackgroundJobRepository>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private async Task<Guid> CrearTrabajoAsync(
        string jobName = "template:prueba",
        int tryCount = 0,
        bool abandonado = false,
        DateTime? completado = null,
        string args = "{}")
    {
        var id = _guids.Create();

        await WithUnitOfWorkAsync(() => _trabajos.InsertAsync(
            new BackgroundJobRecord(id)
            {
                ApplicationName = "Test",
                JobName = jobName,
                JobArgs = args,
                TryCount = (short)tryCount,
                CreationTime = DateTime.UtcNow,
                NextTryTime = DateTime.UtcNow.AddMinutes(1),
                LastTryTime = tryCount > 0 ? DateTime.UtcNow : null,
                IsAbandoned = abandonado,
                CompletionTime = completado
            },
            autoSave: true));

        return id;
    }

    private Task SembrarFalloAsync(
        Guid jobId, string mensaje, int tryCount = 1, int haceMinutos = 0)
    {
        return WithUnitOfWorkAsync(() => _fallos.InsertAsync(new JobFailure(
            _guids.Create(),
            jobId,
            "template:prueba",
            DateTime.UtcNow.AddMinutes(-haceMinutos),
            tryCount,
            "System.InvalidOperationException",
            mensaje,
            "en AlgunSitio.Metodo()")));
    }

    /// <summary>Uno de cada estado, para las pruebas que los recorren todos.</summary>
    private async Task<Dictionary<JobState, Guid>> SembrarUnoDeCadaEstadoAsync()
    {
        return new Dictionary<JobState, Guid>
        {
            [JobState.Esperando] = await CrearTrabajoAsync(),
            [JobState.Reintentando] = await CrearTrabajoAsync(tryCount: 3),
            [JobState.Abandonado] = await CrearTrabajoAsync(tryCount: 11, abandonado: true),
            // Completado TRAS un reintento: es el caso que distingue el orden de las
            // comprobaciones al derivar. Si se mirara TryCount antes que CompletionTime,
            // este se leeria como "reintentando" para siempre.
            [JobState.Completado] = await CrearTrabajoAsync(
                tryCount: 2, completado: DateTime.UtcNow)
        };
    }

    [Fact]
    public async Task El_filtro_por_estado_coincide_con_el_estado_que_se_pinta()
    {
        // EL TEST QUE IMPORTA. El estado no es una columna: se deriva de CompletionTime,
        // IsAbandoned y TryCount. El repositorio lo traduce a un WHERE de SQL y la fila lo
        // calcula en C# con JobEntry.Derivar — dos escrituras de la misma regla. Si se
        // separan, el panel ensena filas etiquetadas "abandonado" al filtrar por
        // "esperando" y nadie se entera hasta que hace falta creerse la pantalla.
        var esperados = await SembrarUnoDeCadaEstadoAsync();

        foreach (var (estado, id) in esperados)
        {
            // El repositorio se llama en crudo (no vía app service) para probar el WHERE
            // de SQL sin nada en medio; por eso hace falta abrir la unidad de trabajo a
            // mano, que en producción la pone el application service.
            var filtradas = await WithUnitOfWorkAsync(() => _consulta.GetRecentAsync(estado, 50));

            filtradas.Select(f => f.Id).ShouldBe([id]);
            // Y la etiqueta que llevan coincide con el filtro que las trajo.
            filtradas.ShouldAllBe(f => f.State == estado);
        }
    }

    [Fact]
    public async Task Sin_filtro_salen_todos()
    {
        await SembrarUnoDeCadaEstadoAsync();

        var todas = await WithUnitOfWorkAsync(() => _consulta.GetRecentAsync(null, 50));

        todas.Count.ShouldBe(4);
    }

    [Fact]
    public async Task El_resumen_cuenta_lo_mismo_que_la_lista()
    {
        await SembrarUnoDeCadaEstadoAsync();
        // Un segundo abandonado, para que el resumen no acierte por casualidad contando 1
        // en todo.
        await CrearTrabajoAsync(tryCount: 11, abandonado: true);

        var resumen = await _panel.GetSummaryAsync();

        resumen.Esperando.ShouldBe(1);
        resumen.Reintentando.ShouldBe(1);
        resumen.Abandonado.ShouldBe(2);
        resumen.Completado.ShouldBe(1);
    }

    [Fact]
    public async Task Cada_trabajo_lleva_SU_ultimo_fallo()
    {
        var uno = await CrearTrabajoAsync(tryCount: 2);
        var otro = await CrearTrabajoAsync(tryCount: 1);

        await SembrarFalloAsync(uno, "el primero, viejo", tryCount: 1, haceMinutos: 30);
        await SembrarFalloAsync(uno, "el ultimo de uno", tryCount: 2, haceMinutos: 1);
        await SembrarFalloAsync(otro, "el de otro", tryCount: 1, haceMinutos: 5);

        var filas = await _panel.GetListAsync(new GetJobsInput { MaxResults = 50 });

        // Si los fallos se cruzaran de fila, el panel senalaria al trabajo equivocado —y
        // alguien reintentaria o borraria el que no era.
        filas.Single(f => f.Id == uno).LastFailure!.Message.ShouldBe("el ultimo de uno");
        filas.Single(f => f.Id == otro).LastFailure!.Message.ShouldBe("el de otro");
    }

    [Fact]
    public async Task Un_trabajo_sin_fallos_no_inventa_ninguno()
    {
        await CrearTrabajoAsync();

        var fila = (await _panel.GetListAsync(new GetJobsInput { MaxResults = 50 })).Single();

        fila.LastFailure.ShouldBeNull();
    }

    [Fact]
    public async Task El_historial_va_del_mas_reciente_al_mas_antiguo()
    {
        var id = await CrearTrabajoAsync(tryCount: 3);

        await SembrarFalloAsync(id, "intento 1", tryCount: 1, haceMinutos: 30);
        await SembrarFalloAsync(id, "intento 2", tryCount: 2, haceMinutos: 20);
        await SembrarFalloAsync(id, "intento 3", tryCount: 3, haceMinutos: 10);

        var historial = await _panel.GetFailureHistoryAsync(id);

        // El orden es la mitad de la informacion: lo que se busca aqui es si el motivo
        // cambio entre intentos o siempre fue el mismo.
        historial.Select(h => h.Message)
            .ShouldBe(["intento 3", "intento 2", "intento 1"]);
    }

    [Fact]
    public async Task Reintentar_lo_saca_del_abandono_sin_borrar_la_cuenta_de_intentos()
    {
        var id = await CrearTrabajoAsync(tryCount: 11, abandonado: true);

        var antes = DateTime.UtcNow;
        var devuelto = await _panel.RetryAsync(id);

        devuelto.State.ShouldBe(nameof(JobState.Reintentando));

        var trabajo = await WithUnitOfWorkAsync(() => _trabajos.GetAsync(id));

        trabajo.IsAbandoned.ShouldBeFalse();
        // Adelantado a ahora: si NextTryTime siguiera en el futuro, el worker no lo cogeria
        // y el boton no haria nada visible.
        trabajo.NextTryTime.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
        trabajo.NextTryTime.ShouldBeGreaterThanOrEqualTo(antes.AddSeconds(-5));
        // La cuenta se conserva: ponerla a cero borraria del panel la senal de que esto
        // lleva dias fallando, que es justo lo que hay que ver antes de reintentar.
        trabajo.TryCount.ShouldBe((short)11);
    }

    [Fact]
    public async Task No_se_puede_reintentar_lo_que_ya_salio_bien()
    {
        // Reintentarlo lo EJECUTARIA otra vez. Un aviso duplicado es ruido; un correo o un
        // cobro duplicado, no — y desde el panel no se distingue cual es cual.
        var id = await CrearTrabajoAsync(completado: DateTime.UtcNow);

        var ex = await Should.ThrowAsync<UserFriendlyException>(() => _panel.RetryAsync(id));
        ex.Message.ShouldContain("ya se completó");
    }

    [Fact]
    public async Task Reintentar_algo_que_ya_no_esta_avisa_en_vez_de_reventar()
    {
        // Dos pestanas del panel: la segunda no debe dar un 500.
        var ex = await Should.ThrowAsync<UserFriendlyException>(
            () => _panel.RetryAsync(Guid.NewGuid()));
        ex.Message.ShouldContain("ya no está en la cola");
    }

    [Fact]
    public async Task Borrar_el_trabajo_NO_borra_su_historial_de_fallos()
    {
        var id = await CrearTrabajoAsync(tryCount: 5, abandonado: true);
        await SembrarFalloAsync(id, "el motivo por el que se borro");

        await _panel.DeleteAsync(id);

        (await _panel.GetListAsync(new GetJobsInput { MaxResults = 50 })).ShouldBeEmpty();

        // A proposito: el historial es lo que permite contestar "esto ya nos paso" dentro
        // de tres semanas. Lo limpia la purga por retencion, no el boton de borrar.
        var historial = await _panel.GetFailureHistoryAsync(id);
        historial.ShouldHaveSingleItem().Message.ShouldBe("el motivo por el que se borro");
    }

    [Fact]
    public async Task Un_mensaje_larguisimo_se_recorta_en_vez_de_reventar()
    {
        // El constructor de JobFailure corre DENTRO del manejo de un fallo. Si lanzara por
        // un mensaje largo —una consulta SQL entera, una respuesta HTTP completa— el
        // trabajo pasaria de "falla" a "falla y ademas no deja rastro de por que".
        var id = await CrearTrabajoAsync();
        var enorme = new string('x', JobConsts.MaxMessageLength * 3);

        await WithUnitOfWorkAsync(() => _fallos.InsertAsync(new JobFailure(
            _guids.Create(), id, new string('n', 500), DateTime.UtcNow, 1,
            new string('t', 500), enorme, new string('s', JobConsts.MaxStackTraceLength * 2))));

        var fallo = (await _panel.GetFailureHistoryAsync(id)).ShouldHaveSingleItem();

        fallo.Message.Length.ShouldBe(JobConsts.MaxMessageLength);
        fallo.ExceptionType.Length.ShouldBe(JobConsts.MaxExceptionTypeLength);
        fallo.StackTrace!.Length.ShouldBe(JobConsts.MaxStackTraceLength);
    }

    [Fact]
    public void Un_fallo_con_fecha_que_no_es_UTC_se_rechaza_al_construirlo()
    {
        // La columna es timestamptz y Npgsql lanza en tiempo de EJECUCION si el Kind no es
        // Utc. Mejor reventar aqui, donde el mensaje dice que pasa.
        Should.Throw<AbpException>(() => new JobFailure(
            Guid.NewGuid(), Guid.NewGuid(), "template:prueba",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local),
            1, "System.Exception", "algo", null));
    }

    [Fact]
    public async Task La_purga_borra_los_fallos_viejos_y_deja_los_recientes()
    {
        var id = await CrearTrabajoAsync();

        await SembrarFalloAsync(id, "viejo", haceMinutos: 60 * 24 * 40);
        await SembrarFalloAsync(id, "reciente", haceMinutos: 5);

        var corte = DateTime.UtcNow.AddDays(-JobConsts.FailureRetentionDays);
        var borrados = await WithUnitOfWorkAsync(() => _fallos.PurgeOlderThanAsync(corte));

        borrados.ShouldBe(1);
        (await _panel.GetFailureHistoryAsync(id))
            .ShouldHaveSingleItem().Message.ShouldBe("reciente");
    }

    [Fact]
    public async Task Un_estado_que_no_existe_se_trata_como_sin_filtro()
    {
        // El estado llega como texto libre desde la API. El panel es de solo lectura: una
        // cadena mal escrita no justifica romper la pantalla.
        await SembrarUnoDeCadaEstadoAsync();

        var filas = await _panel.GetListAsync(
            new GetJobsInput { State = "PatataFrita", MaxResults = 50 });

        filas.Count.ShouldBe(4);
    }
}
