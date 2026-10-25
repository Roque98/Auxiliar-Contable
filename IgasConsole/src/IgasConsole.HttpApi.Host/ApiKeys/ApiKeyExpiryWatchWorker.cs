using System;
using System.Threading.Tasks;
using IgasConsole.ApiKeys;
using IgasConsole.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Guids;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Avisa a cada dueño de que su clave de API está a punto de caducar.
///
/// <para>Hoy una clave caduca en silencio: el script que la usaba empieza a recibir 401 y no
/// hay nada que relacione una cosa con la otra — ni siquiera para quien la creó, porque la
/// fecha de caducidad se eligió meses antes y nadie la recuerda.</para>
///
/// <para>⚠️ Este es el primer aviso **personal** del sistema: va dirigido a un
/// <c>UserId</c>, no a un permiso. Tiene que ser así — una clave es de quien la creó, y
/// avisar a todos los administradores de la caducidad de la clave de otro sería a la vez
/// ruido y una filtración de qué integraciones tiene montadas cada uno.</para>
///
/// <para>No se manda por Telegram, al revés que los avisos operativos: esto no es una
/// incidencia de la instalación, y el canal de Telegram va a los chats de monitoreo, no a la
/// persona. Por eso escribe en el repositorio directamente y no pasa por
/// <see cref="IAvisador"/>.</para>
/// </summary>
public class ApiKeyExpiryWatchWorker : AsyncPeriodicBackgroundWorkerBase
{
    public ApiKeyExpiryWatchWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan
            .FromHours(ApiKeyConsts.AvisoCaducidadIntervaloHoras).TotalMilliseconds;

        // Igual que el vigilante de trabajos abandonados: con un período de doce horas y
        // despliegues frecuentes, sin esto podría no llegar a correr nunca. El freno por
        // clave impide que los reinicios repitan el aviso.
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var unidades = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var unidad = unidades.Begin(requiresNew: true);

        var claves = workerContext.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var avisos = workerContext.ServiceProvider.GetRequiredService<INotificacionRepository>();
        var guids = workerContext.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var ahora = DateTime.UtcNow;
        var hasta = ahora.AddDays(ApiKeyConsts.AvisoCaducidadDias);
        var desdeCuandoCallar = ahora.AddDays(-ApiKeyConsts.AvisoCaducidadSilencioDias);

        var porCaducar = await claves.GetExpiringAsync(ahora, hasta);

        var avisadas = 0;

        foreach (var clave in porCaducar)
        {
            var dias = (int)Math.Ceiling((clave.ExpiresUtc!.Value - ahora).TotalDays);

            var titulo = dias <= 1
                ? $"Tu clave de API «{clave.Name}» caduca hoy"
                : $"Tu clave de API «{clave.Name}» caduca en {dias} días";

            // El freno mira título Y usuario: dos personas pueden tener una clave con el
            // mismo nombre, y sin el usuario la segunda no recibiría nunca su aviso.
            if (await avisos.ExistsRecentAsync(titulo, clave.UserId, desdeCuandoCallar))
            {
                continue;
            }

            await avisos.InsertAsync(new Notificacion(
                guids.Create(),
                // El tenant sale de la CLAVE, no del contexto: el worker corre sin tenant y
                // el aviso tiene que aparecer donde su dueño lo va a mirar.
                clave.TenantId,
                clave.UserId,
                requiredPermission: null,
                NotificationSeverity.Warning,
                titulo,
                $"Después de esa fecha dejará de autenticar y lo que la use empezará a " +
                $"recibir 401. Crea una nueva y cambia el valor donde esté guardada; " +
                $"la clave antigua se puede revocar en cuanto la nueva funcione.",
                url: "/api-keys",
                ahora));

            avisadas++;
        }

        await unidad.CompleteAsync();

        if (avisadas > 0)
        {
            Logger.LogInformation(
                "Avisada la caducidad de {Cuantas} clave(s) de API", avisadas);
        }
    }
}
