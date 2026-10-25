using System;
using System.Threading;
using System.Threading.Tasks;
using IgasConsole.BackgroundJobs;
using IgasConsole.Notifications;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Manda el aviso por los dos canales: lo deja en la campana y lo encola para Telegram.
///
/// <para>El ORDEN importa. Primero la campana, que es una escritura local en la misma base
/// que ya se está tocando; después el encolado de Telegram, que puede fallar y ABP
/// reintentará. Al revés, un problema al guardar el aviso dejaría el mensaje ya en camino y
/// sin rastro dentro de la aplicación.</para>
///
/// <para>Ninguno de los dos canales puede tumbar a quien avisa: un recolector de métricas
/// que revienta porque no pudo avisar deja de recolectar, que es peor que el aviso perdido.
/// Por eso cada canal va en su propio try — y el fallo se registra, no se traga en silencio.</para>
/// </summary>
public class Avisador : IAvisador, ITransientDependency
{
    private readonly INotificacionRepository _avisos;
    private readonly IBackgroundJobManager _trabajos;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _tenantActual;
    private readonly ILogger<Avisador> _logger;

    public Avisador(
        INotificacionRepository avisos,
        IBackgroundJobManager trabajos,
        IGuidGenerator guids,
        ICurrentTenant tenantActual,
        ILogger<Avisador> logger)
    {
        _avisos = avisos;
        _trabajos = trabajos;
        _guids = guids;
        _tenantActual = tenantActual;
        _logger = logger;
    }

    public async Task AvisarAsync(
        string titulo,
        string cuerpo,
        NotificationSeverity severidad,
        string permiso,
        string? url = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _avisos.InsertAsync(
                new Notificacion(
                    _guids.Create(),
                    _tenantActual.Id,
                    userId: null,
                    requiredPermission: permiso,
                    severidad,
                    titulo,
                    cuerpo,
                    url,
                    DateTime.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo guardar el aviso en la aplicación: {Titulo}", titulo);
        }

        try
        {
            // Se ENCOLA en vez de enviarse aquí: si Telegram está caído en este momento, el
            // trabajo lo reintenta con espera creciente. Ver EnviarAvisoDeMonitoreoJob.
            await _trabajos.EnqueueAsync(
                new EnviarAvisoDeMonitoreoArgs { Titulo = titulo, Cuerpo = cuerpo });
        }
        catch (Exception ex)
        {
            // Esto solo salta si la base no responde, no si Telegram falla: de eso ya se
            // ocupa el trabajo reintentando.
            _logger.LogWarning(ex, "No se pudo encolar el aviso de Telegram: {Titulo}", titulo);
        }
    }
}
