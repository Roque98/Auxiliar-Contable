using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Jobs;
using IgasConsole.Notifications;
using IgasConsole.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace IgasConsole.BackgroundJobs;

/// <summary>
/// Avisa cuando la aplicación ha dejado de intentar algo.
///
/// <para>El panel de <c>/jobs</c> ya enseña los trabajos abandonados, pero hay que acordarse
/// de abrirlo. Un trabajo abandonado es, por definición, algo que la aplicación intentó
/// hacer durante dos días y renunció: el enlace de recuperación que alguien pidió y nunca
/// recibió, el aviso de disco lleno que no salió. Es exactamente el tipo de cosa que no
/// puede esperar a que a alguien se le ocurra mirar.</para>
///
/// <para>⚠️ **No avisa por trabajo, sino por tanda.** Un fallo sistémico —el token del bot
/// caducado— abandona todos los trabajos a la vez; un aviso por cada uno convertiría el
/// problema en una avalancha justo cuando hay que leerlo. Se manda un aviso con el recuento
/// y los nombres.</para>
///
/// <para>⚠️ **Y no repite.** El worker corre cada hora y los trabajos abandonados siguen
/// estándolo: sin freno, el mismo problema avisaría veinticuatro veces al día hasta dejar de
/// leerse. Antes de avisar comprueba si ya hay un aviso con ese mismo título en la ventana
/// de silencio. Se compara por TÍTULO —que incluye el recuento— porque es lo que ve la
/// persona: si el número cambia, es un problema distinto y vuelve a avisar.</para>
/// </summary>
public class AbandonedJobsWatchWorker : AsyncPeriodicBackgroundWorkerBase
{
    /// <summary>Cada cuánto se mira la cola.</summary>
    private const int PeriodoHoras = 1;

    /// <summary>
    /// Cuánto se calla tras avisar de lo mismo. Un día: es el tiempo que hace falta para
    /// que a alguien le dé tiempo a mirarlo, y bastante menos que los treinta días que dura
    /// el aviso en la campana.
    /// </summary>
    private const int SilencioHoras = 24;

    /// <summary>Cuántos nombres caben en el cuerpo antes de resumir.</summary>
    private const int MaxNombres = 5;

    public AbandonedJobsWatchWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromHours(PeriodoHoras).TotalMilliseconds;

        // ⚠️ Sin esto la primera comprobación no llegaría hasta dentro de una hora
        // (`AbpAsyncTimer.RunOnStart` es false por defecto, verificado en `refs/abp`), y
        // cada despliegue reiniciaría esa cuenta — con despliegues frecuentes podría no
        // llegar a correr nunca. Además, recién arrancado es justo cuando interesa saber
        // qué quedó a medias. Repetir el aviso no es un riesgo: el freno de las 24 h lo
        // impide.
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        // Un worker no viene envuelto en una unidad de trabajo: hay que abrirla a mano o la
        // primera consulta revienta. Ver CLAUDE.md §6.0.
        var unidades = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var unidad = unidades.Begin(requiresNew: true);

        var consulta = workerContext.ServiceProvider.GetRequiredService<IJobMaintenanceRepository>();

        var abandonados = await consulta.GetRecentAsync(
            JobState.Abandonado, JobConsts.MaxResults);

        if (abandonados.Count == 0)
        {
            return;
        }

        var titulo = abandonados.Count == 1
            ? "Un trabajo en segundo plano se ha abandonado"
            : $"{abandonados.Count} trabajos en segundo plano se han abandonado";

        var avisos = workerContext.ServiceProvider.GetRequiredService<INotificacionRepository>();

        // userId null: este aviso va dirigido a un permiso, no a una persona.
        if (await avisos.ExistsRecentAsync(
                titulo, null, DateTime.UtcNow.AddHours(-SilencioHoras)))
        {
            return;
        }

        var nombres = abandonados
            .Select(t => t.JobName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var listado = string.Join(", ", nombres.Take(MaxNombres));
        if (nombres.Count > MaxNombres)
        {
            listado += $" y {nombres.Count - MaxNombres} más";
        }

        var cuerpo =
            $"La aplicación dejó de reintentarlos tras dos días, así que lo que fueran a " +
            $"hacer no se hizo. Tipos afectados: {listado}.";

        var avisador = workerContext.ServiceProvider.GetRequiredService<IAvisador>();

        await avisador.AvisarAsync(
            titulo,
            cuerpo,
            NotificationSeverity.Error,
            // Quien puede ver la cola es quien puede hacer algo con este aviso.
            IgasConsolePermissions.Jobs.Default,
            url: "/jobs");

        await unidad.CompleteAsync();

        Logger.LogWarning(
            "Avisado: {Cuantos} trabajos abandonados ({Tipos})", abandonados.Count, listado);
    }
}
