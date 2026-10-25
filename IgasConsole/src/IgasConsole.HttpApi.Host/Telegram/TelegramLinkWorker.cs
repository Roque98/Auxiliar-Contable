using System;
using System.Linq;
using System.Threading.Tasks;
using IgasConsole.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace IgasConsole.HealthChecks;

/// <summary>
/// Escucha los mensajes que le llegan al bot y confirma las vinculaciones pendientes.
/// <para>El usuario pulsa un enlace <c>t.me/&lt;bot&gt;?start=&lt;token&gt;</c>, Telegram
/// le manda al bot un <c>/start &lt;token&gt;</c>, y aquí se casa ese token con la cuenta
/// que lo pidió. Es la ÚNICA forma de conocer el chat de alguien: un bot no puede
/// escribir a quien no le habló primero.</para>
/// </summary>
public class TelegramLinkWorker : AsyncPeriodicBackgroundWorkerBase
{
    /// <summary>Segundos que Telegram mantiene abierta la petición esperando mensajes.
    /// Con esto el sondeo cuesta ~2 peticiones por minuto, no una por segundo.</summary>
    private const int TimeoutSondeo = 25;

    /// <summary>
    /// Último update confirmado. Vive en memoria a propósito: si el proceso se reinicia,
    /// Telegram vuelve a entregar lo no confirmado (los guarda 24 h) y como mucho se
    /// reprocesa un mensaje, que es idempotente.
    /// </summary>
    private long _offset;

    public TelegramLinkWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        // Corto: el long poll de dentro es quien espera de verdad.
        Timer.Period = 2000;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var ct = workerContext.CancellationToken;

        var mensajero = sp.GetRequiredService<TelegramMessenger>();

        var updates = await mensajero.GetUpdatesAsync(_offset, TimeoutSondeo, ct);
        if (updates.Count == 0)
        {
            return;
        }

        // Confirmar SIEMPRE, incluso lo que no se procesa: si no, un mensaje que no
        // entendemos bloquearía la cola y volvería en cada sondeo para siempre.
        _offset = updates.Max(u => u.UpdateId) + 1;

        foreach (var update in updates)
        {
            var token = ExtraerTokenDeStart(update.Texto);
            if (token == null)
            {
                continue;
            }

            await ConfirmarAsync(sp, mensajero, update, token, ct);
        }
    }

    /// <summary>Devuelve el token de un "/start &lt;token&gt;", o null si el mensaje es
    /// otra cosa.</summary>
    private static string? ExtraerTokenDeStart(string? texto)
    {
        if (texto.IsNullOrWhiteSpace() || !texto!.StartsWith("/start", StringComparison.Ordinal))
        {
            return null;
        }

        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // "/start" a secas es un saludo, no una vinculación.
        return partes.Length < 2 ? null : partes[1];
    }

    private async Task ConfirmarAsync(
        IServiceProvider sp,
        TelegramMessenger mensajero,
        TelegramUpdate update,
        string token,
        System.Threading.CancellationToken ct)
    {
        var uowManager = sp.GetRequiredService<IUnitOfWorkManager>();
        var repositorio = sp.GetRequiredService<ITelegramLinkRepository>();

        var hash = IgasConsole.Telegram.TelegramLinkAppService.Hash(token);
        var vinculado = false;

        using (var uow = uowManager.Begin(requiresNew: true, isTransactional: true))
        {
            var vinculo = await repositorio.FindByTokenHashAsync(hash, DateTime.UtcNow, ct);

            if (vinculo != null)
            {
                vinculo.Confirmar(update.ChatId, DateTime.UtcNow, update.UserName);
                await repositorio.UpdateAsync(vinculo, autoSave: true, cancellationToken: ct);
                vinculado = true;
            }

            await uow.CompleteAsync(ct);
        }

        // El mismo mensaje para el token bueno y para el malo NO tendría sentido aquí: es
        // el propio usuario quien pulsó el enlace y necesita saber si funcionó. Lo que no
        // se dice nunca es a QUÉ cuenta pertenecía, para no filtrar nada a quien llegue
        // con un token ajeno.
        var respuesta = vinculado
            ? "✅ Listo. Tu cuenta quedó vinculada: aquí te llegará el enlace si alguna vez " +
              "pides recuperar tu contraseña."
            : "❌ Ese enlace de vinculación no es válido o ya caducó. Genera uno nuevo desde la aplicación.";

        await mensajero.SendToChatAsync(update.ChatId, respuesta, ct);

        if (vinculado)
        {
            Logger.LogInformation("Telegram vinculado para el chat {Chat}", update.ChatId);
        }
    }
}
