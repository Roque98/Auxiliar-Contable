using System;
using System.Threading.Tasks;
using AuxiliarContable.Emails;
using AuxiliarContable.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Emailing;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace AuxiliarContable.Contabilidad;

/// <summary>
/// Cada 12 horas recuerda lo que cada cliente dijo que subiría después (<see cref="RecordatorioDeLoQueFalta"/>): aviso en la
/// campana y correo. Corre al arrancar, como los demás vigilantes: con despliegues frecuentes, si no, podría no correr nunca;
/// el freno por persona y cliente impide repetir.
/// </summary>
public class RecordatorioDeLoQueFaltaWorker : AsyncPeriodicBackgroundWorkerBase
{
    public RecordatorioDeLoQueFaltaWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromHours(12).TotalMilliseconds;
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var unidades = sp.GetRequiredService<IUnitOfWorkManager>();
        using var unidad = unidades.Begin(requiresNew: true);
        var porMandar = await sp.GetRequiredService<RecordatorioDeLoQueFalta>().PrepararAsync(DateTime.UtcNow);
        // Los avisos quedan aunque algún correo falle: la campana ya es un recordatorio.
        await unidad.CompleteAsync();

        var correo = sp.GetRequiredService<IEmailSender>();
        var plantillas = sp.GetRequiredService<PlantillasDeCorreo>();
        var consola = (sp.GetRequiredService<IConfiguration>()["App:AngularUrl"] ?? string.Empty).TrimEnd('/');
        var enviados = 0;
        foreach (var r in porMandar)
        {
            try
            {
                var cuerpo = await plantillas.RenderAsync(AuxiliarEmailTemplates.Recordatorio, new
                {
                    nombre = r.Nombre,
                    organizacion = r.Organizacion,
                    total = r.Total,
                    pendientes = r.Pendientes,
                    enlace = consola + r.Url,
                }, $"Te falta subir {r.Total} documento(s) de {r.Organizacion}.");
                await correo.SendAsync(r.Email, $"Te falta subir {r.Total} documento(s) de {r.Organizacion}", cuerpo);
                enviados++;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "No se pudo mandar el recordatorio de documentos pendientes");
            }
        }

        if (porMandar.Count > 0)
        {
            Logger.LogInformation("Recordatorios de documentos pendientes: {Avisos} aviso(s), {Correos} correo(s)", porMandar.Count, enviados);
        }
    }
}
