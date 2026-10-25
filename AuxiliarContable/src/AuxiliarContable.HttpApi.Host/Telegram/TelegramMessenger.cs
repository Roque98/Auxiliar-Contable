using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuxiliarContable.Monitoring;
using AuxiliarContable.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Habla con la API de Telegram para lo que NO son avisos de monitoreo: mensajes a un
/// usuario concreto y datos del bot.
/// <para>Usa el cliente HTTP con nombre <c>TelegramAlertNotifier</c> a propósito: es el
/// que Serilog tiene silenciado en appsettings, y la URL de Telegram lleva el token
/// dentro. Si se usara otro nombre, el token volvería al log en claro.</para>
/// </summary>
public class TelegramMessenger : ITelegramMessenger, ITransientDependency
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingProvider _settingProvider;
    private readonly ISettingManager _settingManager;
    private readonly MonitoringOptions _opciones;
    private readonly ILogger<TelegramMessenger> _logger;

    public TelegramMessenger(
        IHttpClientFactory httpClientFactory,
        ISettingProvider settingProvider,
        ISettingManager settingManager,
        IOptions<MonitoringOptions> opciones,
        ILogger<TelegramMessenger> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingProvider = settingProvider;
        _settingManager = settingManager;
        _opciones = opciones.Value;
        _logger = logger;
    }

    public async Task<string?> ObtenerTokenAsync()
    {
        var token = await _settingProvider.GetOrNullAsync(MonitoringSettingNames.TelegramBotToken);
        return token.IsNullOrWhiteSpace() ? _opciones.TelegramBotToken : token;
    }

    public async Task<bool> SendToChatAsync(
        long chatId, string texto, CancellationToken cancellationToken = default)
    {
        var token = await ObtenerTokenAsync();
        if (token.IsNullOrWhiteSpace())
        {
            _logger.LogWarning("Mensaje de Telegram sin bot configurado; no se envía nada.");
            return false;
        }

        try
        {
            var cliente = Cliente();
            var contenido = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = chatId.ToString(),
                ["text"] = texto,
                // Sin formato: el texto lleva una URL con token y cualquier interpretación
                // de Markdown podría romperla o cambiarla.
                ["disable_web_page_preview"] = "true"
            });

            var respuesta = await cliente.PostAsync(
                $"https://api.telegram.org/bot{token}/sendMessage", contenido, cancellationToken);

            if (respuesta.IsSuccessStatusCode)
            {
                return true;
            }

            // El cuerpo explica el motivo, pero la URL NUNCA se registra: lleva el token.
            _logger.LogWarning(
                "Telegram rechazó el mensaje al chat {Chat} ({Codigo})", chatId, (int)respuesta.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo de red enviando mensaje de Telegram al chat {Chat}", chatId);
            return false;
        }
    }

    public async Task<bool> IsBotConfiguredAsync(CancellationToken cancellationToken = default)
    {
        // Solo ajustes, NADA de red: de esto depende que la pantalla de vinculación
        // cargue al instante en vez de esperar a que Telegram conteste.
        var token = await ObtenerTokenAsync();
        return !token.IsNullOrWhiteSpace();
    }

    public async Task<string?> GetBotUserNameAsync(CancellationToken cancellationToken = default)
    {
        var token = await ObtenerTokenAsync();
        if (token.IsNullOrWhiteSpace())
        {
            return null;
        }

        // El nombre de un bot no cambia salvo que se cambie el bot: se cachea y así solo
        // se sale a la red la primera vez.
        var cacheado = await _settingProvider.GetOrNullAsync(MonitoringSettingNames.TelegramBotUserName);
        if (!cacheado.IsNullOrWhiteSpace())
        {
            return cacheado;
        }

        try
        {
            var cliente = Cliente();
            var respuesta = await cliente.GetAsync(
                $"https://api.telegram.org/bot{token}/getMe", cancellationToken);

            if (!respuesta.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await respuesta.Content.ReadAsStringAsync(cancellationToken);
            using var documento = JsonDocument.Parse(json);

            var nombre = documento.RootElement
                .GetProperty("result")
                .GetProperty("username")
                .GetString();

            if (!nombre.IsNullOrWhiteSpace())
            {
                await GuardarNombreAsync(nombre!);
            }

            return nombre;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo consultar el nombre del bot de Telegram");
            return null;
        }
    }

    private async Task GuardarNombreAsync(string nombre)
    {
        try
        {
            await _settingManager.SetGlobalAsync(MonitoringSettingNames.TelegramBotUserName, nombre);
        }
        catch (Exception ex)
        {
            // No poder cachearlo no puede romper la vinculación: como mucho se vuelve a
            // preguntar a Telegram la próxima vez.
            _logger.LogWarning(ex, "No se pudo cachear el nombre del bot");
        }
    }

    /// <summary>
    /// Sondeo largo de mensajes entrantes. Se eligió esto y no un webhook: un webhook
    /// exige exponer un endpoint público que Telegram invoca —más superficie de ataque,
    /// verificación de secreto y un registro que mantener entre despliegues—, mientras que
    /// esto no abre nada hacia dentro.
    /// <para><paramref name="offset"/> confirma a Telegram los updates ya procesados: sin
    /// él, los mismos mensajes volverían en cada sondeo para siempre.</para>
    /// </summary>
    public async Task<List<TelegramUpdate>> GetUpdatesAsync(
        long offset, int timeoutSegundos, CancellationToken cancellationToken = default)
    {
        var resultado = new List<TelegramUpdate>();

        var token = await ObtenerTokenAsync();
        if (token.IsNullOrWhiteSpace())
        {
            return resultado;
        }

        try
        {
            var cliente = _httpClientFactory.CreateClient(nameof(TelegramAlertNotifier));
            // Un poco mas que el long poll, o el cliente cortaria antes que Telegram.
            cliente.Timeout = TimeSpan.FromSeconds(timeoutSegundos + 10);

            var respuesta = await cliente.GetAsync(
                $"https://api.telegram.org/bot{token}/getUpdates" +
                $"?offset={offset}&timeout={timeoutSegundos}&allowed_updates=%5B%22message%22%5D",
                cancellationToken);

            if (!respuesta.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram rechazó getUpdates ({Codigo})", (int)respuesta.StatusCode);
                return resultado;
            }

            var json = await respuesta.Content.ReadAsStringAsync(cancellationToken);
            using var documento = JsonDocument.Parse(json);

            foreach (var update in documento.RootElement.GetProperty("result").EnumerateArray())
            {
                var updateId = update.GetProperty("update_id").GetInt64();

                if (!update.TryGetProperty("message", out var mensaje))
                {
                    // Aun asi cuenta para el offset: si no, se reintentaria eternamente.
                    resultado.Add(new TelegramUpdate(updateId, 0, null, null));
                    continue;
                }

                var chatId = mensaje.GetProperty("chat").GetProperty("id").GetInt64();
                var texto = mensaje.TryGetProperty("text", out var t) ? t.GetString() : null;
                var usuario = mensaje.TryGetProperty("from", out var f)
                              && f.TryGetProperty("username", out var u)
                    ? u.GetString()
                    : null;

                resultado.Add(new TelegramUpdate(updateId, chatId, texto, usuario));
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado del worker o timeout del sondeo: no es un error.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo sondeando mensajes de Telegram");
        }

        return resultado;
    }

    private HttpClient Cliente()
    {
        var cliente = _httpClientFactory.CreateClient(nameof(TelegramAlertNotifier));
        cliente.Timeout = TimeSpan.FromSeconds(15);
        return cliente;
    }
}

/// <summary>Un mensaje entrante, reducido a lo que hace falta para vincular.</summary>
public record TelegramUpdate(long UpdateId, long ChatId, string? Texto, string? UserName);
