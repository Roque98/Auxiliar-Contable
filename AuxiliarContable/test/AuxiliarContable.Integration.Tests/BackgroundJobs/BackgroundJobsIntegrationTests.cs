using System;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.BackgroundJobs;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Trabajos en segundo plano contra PostgreSQL real.
/// <para>Lo que se prueba aquí no es que ABP sepa encolar —eso es suyo—, sino las dos cosas
/// que en ESTA aplicación pueden salir mal sin que nadie se entere: que el sistema de
/// trabajos no esté realmente conectado, y que un secreto acabe escrito en la tabla.</para>
/// <para>Los trabajos se encolan pero no se ejecutan: ver la nota en
/// <c>AuxiliarContablePostgreSqlTestModule</c>.</para>
/// </summary>
public class BackgroundJobsIntegrationTests : IntegrationTestBase
{
    private readonly IBackgroundJobManager _jobs;
    private readonly IBackgroundJobStore _almacen;

    public BackgroundJobsIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _jobs = GetRequiredService<IBackgroundJobManager>();
        _almacen = GetRequiredService<IBackgroundJobStore>();
    }

    private Task<System.Collections.Generic.List<BackgroundJobInfo>> PendientesAsync()
    {
        // applicationName null = sin filtrar por aplicación.
        return WithUnitOfWorkAsync(() => _almacen.GetWaitingJobsAsync(null, 100));
    }

    [Fact]
    public void El_gestor_de_trabajos_es_el_de_verdad()
    {
        // ABP registra NullBackgroundJobManager cuando el sistema no está montado, y ese
        // LANZA al encolar. Si alguien quitara un modulo por el camino, todo seguiria
        // compilando y los envios empezarian a fallar solo en ejecucion: este test es lo
        // que convierte ese fallo silencioso en un test rojo.
        _jobs.ShouldNotBeOfType<NullBackgroundJobManager>();
    }

    [Fact]
    public async Task Un_aviso_de_monitoreo_se_encola_con_su_nombre_fijado()
    {
        await WithUnitOfWorkAsync(() => _jobs.EnqueueAsync(new EnviarAvisoDeMonitoreoArgs
        {
            Titulo = "CPU alta",
            Cuerpo = "95% durante 3 minutos"
        }));

        var pendientes = await PendientesAsync();
        var trabajo = pendientes.ShouldHaveSingleItem();

        // El nombre viene de [BackgroundJobName], no del nombre del tipo. Si alguien
        // quitara el atributo, este test se entera: pasaria a ser el FullName y los
        // trabajos ya encolados en produccion quedarian huerfanos tras el despliegue.
        trabajo.JobName.ShouldBe("template:aviso-monitoreo");
        trabajo.JobArgs.ShouldContain("CPU alta");
    }

    [Fact]
    public async Task El_enlace_de_recuperacion_se_encola_SIN_el_token()
    {
        // EL TEST QUE IMPORTA. Los argumentos se guardan como JSON en AbpBackgroundJobs, y
        // el token de restablecimiento es una credencial: quien lo tenga cambia la
        // contraseña de la cuenta. Por eso el token NO viaja en los argumentos — se genera
        // dentro del trabajo, justo antes de enviarlo.
        var userId = Guid.NewGuid();

        await WithUnitOfWorkAsync(() => _jobs.EnqueueAsync(new EnviarEnlaceRecuperacionArgs
        {
            UserId = userId,
            TenantId = null,
            AppName = "MVC"
        }));

        var trabajo = (await PendientesAsync()).ShouldHaveSingleItem();

        trabajo.JobName.ShouldBe("template:enlace-recuperacion");
        trabajo.JobArgs.ShouldContain(userId.ToString());

        // Ni el token ni nada que se le parezca. Se comprueba sobre el JSON tal cual queda
        // guardado, que es lo que de verdad leeria quien tuviera acceso a la base.
        var json = trabajo.JobArgs.ToLowerInvariant();
        json.ShouldNotContain("token");
        json.ShouldNotContain("resettoken");
        json.ShouldNotContain("password");
    }

    [Fact]
    public async Task Los_argumentos_del_enlace_llevan_el_tenant()
    {
        // Un trabajo NO hereda el tenant de la peticion que lo encolo. Sin este dato, buscar
        // al usuario dentro del trabajo no lo encontraria y el enlace no saldria nunca.
        var tenantId = Guid.NewGuid();

        await WithUnitOfWorkAsync(() => _jobs.EnqueueAsync(new EnviarEnlaceRecuperacionArgs
        {
            UserId = Guid.NewGuid(),
            TenantId = tenantId
        }));

        var trabajo = (await PendientesAsync()).ShouldHaveSingleItem();

        trabajo.JobArgs.ShouldContain(tenantId.ToString());
    }

    [Fact]
    public async Task Un_trabajo_encolado_sobrevive_a_la_peticion()
    {
        // La razon de ser de todo esto: el envio deja de depender de que la peticion siga
        // viva. Se comprueba que la fila queda persistida de verdad, no en memoria.
        await WithUnitOfWorkAsync(() => _jobs.EnqueueAsync(new EnviarAvisoDeMonitoreoArgs
        {
            Titulo = "Disco lleno",
            Cuerpo = "Queda un 3%"
        }));

        // Unidad de trabajo distinta: si esto solo viviera en memoria, aqui no habria nada.
        var pendientes = await PendientesAsync();

        pendientes.Count.ShouldBe(1);
        pendientes.Single().TryCount.ShouldBe((short)0);
        pendientes.Single().IsAbandoned.ShouldBeFalse();
    }
}
