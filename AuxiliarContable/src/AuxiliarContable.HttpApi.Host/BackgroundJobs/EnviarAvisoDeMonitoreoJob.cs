using System.Threading.Tasks;
using AuxiliarContable.Monitoring;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace AuxiliarContable.BackgroundJobs;

/// <summary>
/// Entrega un aviso de monitoreo fuera del ciclo de recolección.
///
/// <para>Antes el recolector llamaba a <c>IAlertNotifier</c> directamente y se tragaba la
/// excepción para no tumbar la recolección — con lo cual, si Telegram estaba caído justo en
/// ese minuto, el aviso se perdía y solo quedaba una línea de warning. Un sistema de avisos
/// que pierde avisos en silencio es peor que no tenerlo, porque genera confianza infundada.</para>
///
/// <para>Encolado, ABP reintenta con espera creciente (1 min, 2, 4…) hasta darlo por
/// abandonado a los 2 días. Un corte de red de unos minutos ya no cuesta el aviso.</para>
///
/// <para>OJO: el botón «probar» de la pantalla de ajustes NO pasa por aquí y sigue llamando
/// al notificador directamente. Quien pulsa ese botón está esperando una respuesta y
/// necesita ver el error de verdad, no un «encolado» que no le dice nada.</para>
/// </summary>
public class EnviarAvisoDeMonitoreoJob : AsyncBackgroundJob<EnviarAvisoDeMonitoreoArgs>, ITransientDependency
{
    private readonly IAlertNotifier _avisos;
    private readonly IUnitOfWorkManager _unidadesDeTrabajo;

    public EnviarAvisoDeMonitoreoJob(IAlertNotifier avisos, IUnitOfWorkManager unidadesDeTrabajo)
    {
        _avisos = avisos;
        _unidadesDeTrabajo = unidadesDeTrabajo;
    }

    public override async Task ExecuteAsync(EnviarAvisoDeMonitoreoArgs args)
    {
        // ⚠️ La unidad de trabajo hay que ABRIRLA A MANO: un trabajo en segundo plano no
        // viene envuelto en una, al contrario que un application service. Aquí hace falta
        // porque el notificador lee los ajustes (token y chats) de la base y además apunta
        // ahí el resultado del último envío.
        using var unidad = _unidadesDeTrabajo.Begin(requiresNew: true);

        // Sin try/catch a propósito: aquí una excepción es lo DESEABLE. Es la señal con la
        // que ABP decide reintentar. Atraparla convertiría el job en el mismo agujero
        // silencioso que había antes.
        await _avisos.NotifyAsync(args.Titulo, args.Cuerpo);

        await unidad.CompleteAsync();
    }
}
