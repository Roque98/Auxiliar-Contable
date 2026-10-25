using System;
using System.Threading.Tasks;
using IgasConsole.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.SettingManagement;

namespace IgasConsole.Monitoring;

/// <summary>
/// Configuración de los avisos desde la web. Los valores viven en AbpSettings (globales,
/// no por tenant): el bot avisa del estado del servidor, que es uno para todos.
/// </summary>
[Authorize(IgasConsolePermissions.Metrics.ManageSettings)]
public class MonitoringSettingsAppService : IgasConsoleAppService, IMonitoringSettingsAppService
{
    private readonly ISettingManager _settingManager;
    private readonly IAlertNotifier _alertNotifier;

    public MonitoringSettingsAppService(ISettingManager settingManager, IAlertNotifier alertNotifier)
    {
        _settingManager = settingManager;
        _alertNotifier = alertNotifier;
    }

    public async Task<MonitoringSettingsDto> GetAsync()
    {
        var token = await _settingManager.GetOrNullGlobalAsync(MonitoringSettingNames.TelegramBotToken);
        var chats = await _settingManager.GetOrNullGlobalAsync(MonitoringSettingNames.TelegramChatIds);
        var activos = await _settingManager.GetOrNullGlobalAsync(MonitoringSettingNames.AlertsEnabled);
        var ultimo = await _settingManager.GetOrNullGlobalAsync(MonitoringSettingNames.LastAlertResult);

        var dto = new MonitoringSettingsDto
        {
            AlertsEnabled = activos == "true",
            HasBotToken = !token.IsNullOrWhiteSpace(),
            // Solo el final del token, lo justo para reconocer cuál está puesto.
            BotTokenHint = Pista(token),
            ChatIds = chats
        };

        RellenarUltimoAviso(dto, ultimo);

        return dto;
    }

    /// <summary>
    /// Un JSON corrupto aquí no puede romper la pantalla de ajustes: se ignora y como
    /// mucho se deja de ver cuándo fue el último aviso.
    /// </summary>
    private static void RellenarUltimoAviso(MonitoringSettingsDto dto, string? json)
    {
        if (json.IsNullOrWhiteSpace())
        {
            return;
        }

        try
        {
            var ultimo = System.Text.Json.JsonSerializer.Deserialize<UltimoAvisoInfo>(json!);

            if (ultimo != null)
            {
                dto.LastAlertUtc = ultimo.Utc;
                dto.LastAlertOk = ultimo.Ok;
                dto.LastAlertDetail = ultimo.Detalle;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Se queda sin rellenar, que es exactamente lo que significa "no lo sé".
        }
    }

    public async Task<MonitoringSettingsDto> UpdateAsync(UpdateMonitoringSettingsDto input)
    {
        // Un token en blanco NO borra el guardado: el formulario nunca recibe el valor
        // real, así que enviarlo vacío significa "no lo toques", no "bórralo".
        if (!input.BotToken.IsNullOrWhiteSpace())
        {
            await _settingManager.SetGlobalAsync(
                MonitoringSettingNames.TelegramBotToken, input.BotToken!.Trim());

            // Token nuevo puede significar bot nuevo: se tira la caché del nombre para
            // que no se sigan construyendo enlaces t.me/ apuntando al bot anterior.
            await _settingManager.SetGlobalAsync(MonitoringSettingNames.TelegramBotUserName, null);
        }

        await _settingManager.SetGlobalAsync(
            MonitoringSettingNames.TelegramChatIds, input.ChatIds?.Trim());

        // Solo si viene. null = "no toques el interruptor", que es lo que manda el
        // formulario cuando el usuario está cambiando el token y no los avisos.
        if (input.AlertsEnabled.HasValue)
        {
            await _settingManager.SetGlobalAsync(
                MonitoringSettingNames.AlertsEnabled,
                input.AlertsEnabled.Value ? "true" : "false");
        }

        return await GetAsync();
    }

    public async Task<SendTestAlertResultDto> SendTestAsync()
    {
        try
        {
            await _alertNotifier.NotifyAsync(
                "🔔 Prueba",
                $"Aviso de prueba de IgasConsole — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. " +
                "Si lees esto, los avisos de monitoreo están bien configurados.");

            return new SendTestAlertResultDto { Success = true, Message = "Mensaje enviado." };
        }
        catch (Exception ex)
        {
            // El motivo se le devuelve al usuario: descubrir aquí que el chat id está mal
            // es justo el objetivo del botón.
            return new SendTestAlertResultDto { Success = false, Message = ex.Message };
        }
    }

    private static string? Pista(string? token)
    {
        if (token.IsNullOrWhiteSpace())
        {
            return null;
        }

        return token!.Length <= 6 ? "••••" : "••••" + token[^4..];
    }
}
