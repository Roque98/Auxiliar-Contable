using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using AuxiliarContable.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Guids;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AuxiliarContable.BackgroundJobs;

/// <summary>
/// El worker de trabajos de ABP, más una fila en <c>AppJobFailures</c> cada vez que un
/// intento sale mal.
///
/// <para>⚠️ ESTA ES LA PIEZA QUE HACE ÚTIL EL PANEL. <c>AbpBackgroundJobs</c> guarda cuántas
/// veces se intentó y si se abandonó, pero NO por qué (verificado en <c>refs/abp</c>:
/// <c>BackgroundJobRecord</c> no tiene ningún campo para la excepción). Sin esto, la
/// pantalla diría «falló cinco veces» y habría que ir al log del servidor igualmente, que
/// es exactamente el trabajo que se quería ahorrar.</para>
///
/// <para>Se hereda envolviendo <c>TryExecuteJobAsync</c> en vez de copiar su cuerpo. Copiarlo
/// habría atado el reintento con espera creciente, el abandono a los dos días y el manejo
/// del serializador a una versión concreta de ABP, y esas reglas seguirían pareciendo suyas
/// mientras dejaban de serlo.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IBackgroundJobWorker))]
public class WorkerQueGuardaLosFallos : BackgroundJobWorker, ITransientDependency
{
    /// <summary>
    /// Fallos ocurridos ANTES de llegar a ejecutar el trabajo: el nombre guardado en la
    /// fila ya no corresponde a ningún trabajo registrado, o los argumentos no se pueden
    /// deserializar. ABP los trata aparte (<c>HandleJobErrorAsync</c>) y abandona el
    /// trabajo al primer intento, sin reintentos.
    ///
    /// <para>Van por un diccionario y no por <see cref="IUltimoFalloDeJob"/> porque ese
    /// método no recibe el proveedor de servicios y este worker se resuelve del contenedor
    /// raíz, así que no puede inyectar nada con ámbito. La clave es el id del trabajo, lo
    /// que además lo deja a salvo si algún día se ejecutan trabajos en paralelo.</para>
    /// </summary>
    private readonly ConcurrentDictionary<Guid, Exception> _erroresPrevios = new();

    public WorkerQueGuardaLosFallos(
        AbpAsyncTimer timer,
        IOptions<AbpBackgroundJobOptions> jobOptions,
        IOptions<AbpBackgroundJobWorkerOptions> workerOptions,
        IServiceScopeFactory serviceScopeFactory,
        IAbpDistributedLock distributedLock)
        : base(timer, jobOptions, workerOptions, serviceScopeFactory, distributedLock)
    {
    }

    protected override async Task HandleJobErrorAsync(
        IBackgroundJobStore store, BackgroundJobInfo jobInfo, Exception ex)
    {
        _erroresPrevios[jobInfo.Id] = ex;
        await base.HandleJobErrorAsync(store, jobInfo, ex);
    }

    protected override async Task TryExecuteJobAsync(
        PeriodicBackgroundWorkerContext workerContext,
        IBackgroundJobStore store,
        BackgroundJobInfo jobInfo,
        IBackgroundJobExecuter jobExecuter,
        IClock clock,
        IBackgroundJobSerializer serializer)
    {
        await base.TryExecuteJobAsync(workerContext, store, jobInfo, jobExecuter, clock, serializer);

        // base ha mutado jobInfo y ha escrito (o borrado) la fila. Se mira el resultado en
        // vez de repetir su lógica.
        _erroresPrevios.TryRemove(jobInfo.Id, out var errorPrevio);
        var excepcion = errorPrevio ?? TomarFalloDelEjecutor(workerContext);

        if (jobInfo.CompletionTime != null)
        {
            // Terminó bien y se conserva como historial.
            return;
        }

        try
        {
            await GuardarFalloAsync(workerContext, jobInfo, excepcion);
        }
        catch (Exception ex)
        {
            // Anotar el fallo no puede convertirse en un fallo. Si esto reventara, se
            // perdería además el trabajo de base —que ya dejó la fila lista para el
            // siguiente intento— y un problema de la libreta se cobraría el original.
            Logger.LogException(ex);
        }
    }

    private static Exception? TomarFalloDelEjecutor(PeriodicBackgroundWorkerContext workerContext)
    {
        // Del mismo ámbito del que salió el ejecutor, así que es la misma instancia.
        return workerContext.ServiceProvider.GetRequiredService<IUltimoFalloDeJob>().Tomar();
    }

    private static async Task GuardarFalloAsync(
        PeriodicBackgroundWorkerContext workerContext,
        BackgroundJobInfo jobInfo,
        Exception? excepcion)
    {
        var store = workerContext.ServiceProvider.GetRequiredService<IBackgroundJobStore>();

        // Sin CompletionTime hay dos posibilidades: que el intento fallara, o que el
        // trabajo saliera bien y ABP borrara la fila (es lo que hace cuando no se
        // conservan los completados). Se distinguen mirando si la fila sigue estando.
        if (await store.FindAsync(jobInfo.Id) == null)
        {
            return;
        }

        // Un worker NO viene envuelto en una unidad de trabajo: hay que abrirla a mano o la
        // primera consulta revienta con "A DbContext can only be created inside a unit of
        // work!". Ver CLAUDE.md §6.0.
        var unidades = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var unidad = unidades.Begin(requiresNew: true);

        var fallos = workerContext.ServiceProvider.GetRequiredService<IJobFailureRepository>();
        var guids = workerContext.ServiceProvider.GetRequiredService<IGuidGenerator>();

        await fallos.InsertAsync(new JobFailure(
            guids.Create(),
            jobInfo.Id,
            jobInfo.JobName,
            DateTime.UtcNow,
            jobInfo.TryCount,
            excepcion?.GetType().FullName ?? "(no capturada)",
            excepcion?.Message ?? "El intento falló pero no se pudo capturar la excepción.",
            excepcion?.ToString()));

        await unidad.CompleteAsync();
    }
}
