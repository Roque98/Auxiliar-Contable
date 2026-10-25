using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuxiliarContable.BackgroundJobs;
using AuxiliarContable.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Guids;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

using AuxiliarContable.Notifications;
using AuxiliarContable.Permissions;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Bloquea solo, cada minuto, las IPs que se han pasado de la raya.
///
/// <para>La señal son los rechazos que ya registra el rate limiter. La diferencia entre una
/// cosa y la otra es el tiempo: el rate limiter frena a cualquiera durante un minuto y
/// olvida; esto se fija en quien insiste durante una ventana larga y lo aparta de verdad.</para>
///
/// <para>Va APAGADO por defecto (<c>AutoBlockOptions.IsEnabled</c>). Encenderlo significa
/// que la aplicación empieza a dejar gente fuera sin que nadie lo apruebe, y eso hay que
/// quererlo a propósito.</para>
/// </summary>
public class AutoBlockWorker : AsyncPeriodicBackgroundWorkerBase
{
    /// <summary>Cuándo corrió por última vez la señal de VOLUMEN. El worker late al ritmo de
    /// la señal de comportamiento (más rápido); esta se salta los ticks intermedios porque
    /// sus datos —rollups por minuto— no cambian entre medias. <c>AbpAsyncTimer</c> no solapa
    /// ejecuciones, así que un campo simple sin lock basta.</summary>
    private DateTime _ultimoVolumenUtc = DateTime.MinValue;

    public AutoBlockWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        // Late al ritmo de la señal de sondeo, que es la que de verdad caza a los
        // escaneadores (§ comentario en DoWorkAsync). La señal de volumen se autolimita
        // dentro a AutoBlockEvaluationSeconds.
        Timer.Period = (int)TimeSpan
            .FromSeconds(SecurityConsts.AutoBlockScanEvaluationSeconds).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var opciones = sp.GetRequiredService<IOptions<AutoBlockOptions>>().Value;

        if (!opciones.IsEnabled)
        {
            return;
        }

        var uowManager = sp.GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true);

        var fuente = sp.GetRequiredService<IAbusiveIpSource>();
        var repositorio = sp.GetRequiredService<IBlockedIpRepository>();
        var guids = sp.GetRequiredService<IGuidGenerator>();

        var desde = DateTime.UtcNow.AddMinutes(-opciones.WindowMinutes);

        // DOS señales distintas, y hacen falta las dos:
        //
        //  1. Rechazos del rate limiter -> detecta VOLUMEN (avalanchas).
        //  2. Sondeo de rutas          -> detecta COMPORTAMIENTO (buscar credenciales).
        //
        // ⚠️ La segunda se añadió porque la primera, sola, no bloqueaba a nadie: medido en
        // producción, los escaneadores iban a 378 y 591 peticiones por minuto con la cuota
        // en 600, así que NUNCA llegaban a ser rechazados y por tanto nunca se contaban.
        // Probaban /.env, /credentials/aws.env, /.ssh/id_ecdsa.pub… con un 92-100 % de
        // fallo, mientras los usuarios reales fallaban un 0-2 %.
        //
        // Corren a RITMOS distintos: la 2 en cada tick (sus datos, una fila por petición en
        // AbpAuditLogs, llegan sin parar y esperar alarga lo que el escáner alcanza a probar
        // antes del corte); la 1 solo cada AutoBlockEvaluationSeconds, porque su fuente es un
        // rollup por minuto y releerlo antes no ve nada nuevo.
        var candidatas = new List<(string Ip, string Motivo, string Etiqueta)>();

        var ahora = DateTime.UtcNow;
        if (ahora - _ultimoVolumenUtc >=
            TimeSpan.FromSeconds(SecurityConsts.AutoBlockEvaluationSeconds))
        {
            _ultimoVolumenUtc = ahora;

            foreach (var abusiva in await fuente.GetAsync(
                         desde, opciones.RejectionThreshold, opciones.MaxBlocksPerRun))
            {
                candidatas.Add((
                    abusiva.IpAddress,
                    $"Automático: {abusiva.Rechazos} peticiones rechazadas en {opciones.WindowMinutes} min",
                    $"{abusiva.IpAddress} ({abusiva.Rechazos} rechazos)"));
            }
        }

        if (opciones.ScanDetectionEnabled)
        {
            foreach (var escaneador in await fuente.GetScannersAsync(
                         desde, opciones.ScanFailureThreshold, opciones.ScanMinFailureRate,
                         opciones.MaxBlocksPerRun))
            {
                // El motivo dice las TRES cifras que justifican el bloqueo. Dentro de tres
                // meses, "Automático: 40 rechazos" no permite decidir si quitarlo; esto sí.
                candidatas.Add((
                    escaneador.IpAddress,
                    $"Automático: sondeo de rutas — {escaneador.Fallos} fallos en " +
                    $"{escaneador.RutasDistintas} rutas distintas ({escaneador.PorcentajeFallo}% de fallo) " +
                    $"en {opciones.WindowMinutes} min",
                    $"{escaneador.IpAddress} ({escaneador.Fallos} fallos, {escaneador.PorcentajeFallo}%)"));
            }
        }

        if (candidatas.Count == 0)
        {
            return;
        }

        var nuncaBloquear = new HashSet<string>(
            opciones.NeverBlock ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        var bloqueadas = new List<string>();
        // Una IP puede aparecer por las dos señales: se bloquea una vez, con el motivo de
        // la primera que la vio.
        var yaVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (ip, motivo, etiqueta) in candidatas)
        {
            if (nuncaBloquear.Contains(ip) || !yaVistas.Add(ip))
            {
                // El loopback vive en NeverBlock de serie: por ahí entran los health
                // checks, y bloquearlo dejaría la aplicación marcada como caída por sí misma.
                continue;
            }

            var existente = await repositorio.FindByIpAsync(ip);
            if (existente != null)
            {
                // Ya está en la lista. Se respeta lo que haya, incluso si un administrador
                // acaba de quitarle la caducidad o de ponerla a mano: el evaluador no pisa
                // decisiones humanas.
                continue;
            }

            // Siempre con fecha de fin. Un veto automático indefinido convierte un umbral
            // mal puesto en un problema permanente que nadie recuerda haber creado.
            var bloqueo = new BlockedIp(
                guids.Create(),
                ip,
                motivo,
                DateTime.UtcNow.AddHours(opciones.BlockHours),
                isAutomatic: true);

            await repositorio.InsertAsync(bloqueo, autoSave: true);
            bloqueadas.Add(etiqueta);

            Logger.LogWarning("Bloqueo automático de {Ip}: {Motivo}", ip, motivo);

            if (bloqueadas.Count >= opciones.MaxBlocksPerRun)
            {
                // Cortafuegos contra el propio mecanismo: ante un ataque distribuido, sin
                // tope una sola pasada llenaría la tabla y el panel dejaría de ser legible
                // justo cuando más falta hace.
                break;
            }
        }

        if (bloqueadas.Count == 0)
        {
            await uow.CompleteAsync();
            return;
        }

        // ⚠️ EL AVISO VA DENTRO DE LA UNIDAD DE TRABAJO, ANTES DE CERRARLA.
        //
        // Estaba después, y por eso el aviso NUNCA llegaba a la campana: escribir la
        // notificación necesita un DbContext, y con la unidad ya completada reventaba con
        // «A DbContext can only be created inside a unit of work!». Avisador se traga esa
        // excepción a propósito —un canal caído no puede tumbar al worker que bloquea— así
        // que el fallo no salía por ningún sitio: las IPs se bloqueaban bien y el aviso
        // desaparecía en silencio. Solo se vio ejecutándolo y mirando el log.
        var avisador = sp.GetRequiredService<IAvisador>();
        await avisador.AvisarAsync(
            $"🚫 {bloqueadas.Count} IP(s) bloqueada(s) automáticamente",
            string.Join("\n", bloqueadas) +
                $"\n\nDuran {opciones.BlockHours} h. Se pueden quitar desde Seguridad.",
            // Que la aplicación vete a alguien sola no es un fallo, pero sí algo que hay que
            // poder revisar: quien lo lea decide si el umbral está bien puesto.
            NotificationSeverity.Warning,
            AuxiliarContablePermissions.Security.BlockedIps,
            url: "/security");

        await uow.CompleteAsync();

        // El refresco de la caché va DESPUÉS de cerrar: el middleware decide contra una
        // copia en memoria, y no tiene sentido cargarla con filas que aún podrían
        // deshacerse si la unidad de trabajo fallara al confirmar.
        await sp.GetRequiredService<IBlockedIpCache>().RefrescarAsync();
    }
}
