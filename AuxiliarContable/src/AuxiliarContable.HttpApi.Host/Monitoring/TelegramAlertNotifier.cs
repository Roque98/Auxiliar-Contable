using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Envía los avisos por Telegram. Se eligió Telegram y no correo porque no depende de un
/// servidor SMTP y llega al móvil al instante.
/// <para>La configuración se lee primero de los ajustes (editables desde la web, guardados
/// cifrados en AbpSettings) y solo si están vacíos se cae a appsettings/variables de
/// entorno. Ese orden permite arrancar un servidor nuevo con variables de entorno y luego
/// pasar a gestionarlo desde la UI sin tocar el servidor.</para>
/// </summary>
public class TelegramAlertNotifier : IAlertNotifier, ITransientDependency
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingProvider _settingProvider;
    private readonly ISettingManager _settingManager;
    private readonly MonitoringOptions _opciones;
    private readonly ILogger<TelegramAlertNotifier> _logger;

    public TelegramAlertNotifier(
        IHttpClientFactory httpClientFactory,
        ISettingProvider settingProvider,
        ISettingManager settingManager,
        IOptions<MonitoringOptions> opciones,
        ILogger<TelegramAlertNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingProvider = settingProvider;
        _settingManager = settingManager;
        _opciones = opciones.Value;
        _logger = logger;
    }

    public async Task NotifyAsync(string titulo, string cuerpo, CancellationToken cancellationToken = default)
    {
        // ISettingProvider descifra el token al leerlo; nunca sale de este proceso salvo
        // hacia la API de Telegram.
        var token = await _settingProvider.GetOrNullAsync(MonitoringSettingNames.TelegramBotToken);
        if (token.IsNullOrWhiteSpace())
        {
            token = _opciones.TelegramBotToken;
        }

        var chatsCrudo = await _settingProvider.GetOrNullAsync(MonitoringSettingNames.TelegramChatIds);
        if (chatsCrudo.IsNullOrWhiteSpace())
        {
            chatsCrudo = _opciones.TelegramChatId;
        }

        if (token.IsNullOrWhiteSpace() || chatsCrudo.IsNullOrWhiteSpace())
        {
            _logger.LogWarning("Aviso de monitoreo sin canal configurado: {Titulo} — {Cuerpo}", titulo, cuerpo);
            await RegistrarResultadoAsync(false, "No hay bot o chat configurado.", cancellationToken);
            return;
        }

        var chats = chatsCrudo!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        var fallos = new List<string>();

        foreach (var chat in chats)
        {
            var error = await EnviarAUnChatAsync(token!, chat, titulo, cuerpo, cancellationToken);
            if (error != null)
            {
                fallos.Add($"{chat}: {error}");
            }
        }

        // Que un chat falle no puede impedir que los demás reciban el aviso, pero el
        // resultado tiene que llegar a quien pulsó "probar".
        if (fallos.Count > 0)
        {
            var detalle = string.Join(" · ", fallos);
            await RegistrarResultadoAsync(false, detalle, cancellationToken);
            throw new UserFriendlyException("No se pudo entregar el aviso a: " + detalle);
        }

        // El ÉXITO también se registra, y es el punto de todo esto: la única traza que
        // había del envío la escribía el logging de IHttpClientFactory, y esa se silenció
        // a propósito porque su URL lleva el token dentro. Sin esta línea, un canal de
        // avisos que deja de funcionar no se distingue de uno que no tiene nada que decir.
        _logger.LogInformation(
            "Aviso de monitoreo entregado a {Chats} chat(s): {Titulo}", chats.Count, titulo);

        await RegistrarResultadoAsync(true, $"Entregado a {chats.Count} chat(s).", cancellationToken);
    }

    /// <summary>
    /// Guarda el resultado del último intento para que la página pueda mostrarlo. Nunca
    /// puede tumbar el aviso: si esto falla, el mensaje ya se envió igual.
    /// </summary>
    private async Task RegistrarResultadoAsync(bool ok, string detalle, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(new UltimoAvisoInfo
            {
                Utc = DateTime.UtcNow,
                Ok = ok,
                Detalle = detalle.Length > 200 ? detalle[..200] : detalle
            });

            await _settingManager.SetGlobalAsync(MonitoringSettingNames.LastAlertResult, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo registrar el resultado del último aviso");
        }
    }

    private async Task<string?> EnviarAUnChatAsync(
        string token, string chatId, string titulo, string cuerpo, CancellationToken cancellationToken)
    {
        try
        {
            // El nombre del cliente NO es decorativo: Serilog silencia la categoria
            // "System.Net.Http.HttpClient.TelegramAlertNotifier" (ver appsettings.json).
            // El logging propio de IHttpClientFactory registra el URI de cada peticion a
            // nivel Information, y aqui el URI LLEVA EL TOKEN DENTRO. Sin ese override, el
            // token acaba en claro en el journal — cifrarlo en la base no sirve de nada si
            // se filtra por el log. Si renombras esta clase, renombra tambien el override.
            var cliente = _httpClientFactory.CreateClient(nameof(TelegramAlertNotifier));
            cliente.Timeout = TimeSpan.FromSeconds(10);

            var contenido = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = chatId,
                ["text"] = $"{titulo}\n{cuerpo}"
            });

            var respuesta = await cliente.PostAsync(
                $"https://api.telegram.org/bot{token}/sendMessage", contenido, cancellationToken);

            if (respuesta.IsSuccessStatusCode)
            {
                return null;
            }

            // El cuerpo de error de Telegram explica el motivo (chat id inexistente, bot
            // bloqueado, token inválido). La URL NUNCA se registra: lleva el token dentro.
            var detalle = await respuesta.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Telegram rechazó el aviso al chat {Chat} ({Codigo})", chatId, (int)respuesta.StatusCode);
            return Resumir(detalle);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo de red enviando el aviso al chat {Chat}", chatId);
            return ex.Message;
        }
    }

    /// <summary>Se queda con la descripción de Telegram y descarta el resto del JSON.</summary>
    private static string Resumir(string detalle)
    {
        const string marca = "\"description\":\"";
        var i = detalle.IndexOf(marca, StringComparison.Ordinal);
        if (i < 0)
        {
            return detalle.Length > 120 ? detalle[..120] : detalle;
        }

        var desde = i + marca.Length;
        var hasta = detalle.IndexOf('"', desde);
        return hasta > desde ? detalle[desde..hasta] : detalle[desde..];
    }
}
