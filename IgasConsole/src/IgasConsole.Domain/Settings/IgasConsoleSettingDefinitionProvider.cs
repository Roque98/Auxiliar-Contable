using IgasConsole.Branding;
using IgasConsole.Monitoring;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace IgasConsole.Settings;

public class IgasConsoleSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        // El idioma por defecto de ABP es el inglés: sin esto, quien abre el login
        // directamente (sin pasar por el Angular) lo veía en inglés.
        var idioma = context.GetOrNull(LocalizationSettingNames.DefaultLanguage);
        if (idioma != null)
        {
            idioma.DefaultValue = "es";
        }

        context.Add(new SettingDefinition(IgasConsoleSettings.VersionPermisosSembrados, "0"));

        // Avisos de monitoreo. Se editan desde /metrics en la web y se guardan en
        // AbpSettings, no en appsettings: cambiar de bot o de chat no debe exigir un
        // despliegue.
        context.Add(
            new SettingDefinition(
                MonitoringSettingNames.TelegramBotToken,
                defaultValue: null,
                // isEncrypted: ABP lo cifra en la base con StringEncryption:DefaultPassPhrase.
                // Un token de bot en claro en una tabla es una credencial regalada a
                // cualquiera que lea la base o una copia de seguridad.
                isEncrypted: true),

            new SettingDefinition(
                MonitoringSettingNames.TelegramChatIds,
                defaultValue: null),

            // Cache del nombre del bot. No lo edita nadie: lo rellena el mensajero la
            // primera vez y se borra al cambiar el token.
            new SettingDefinition(
                MonitoringSettingNames.TelegramBotUserName,
                defaultValue: null),

            new SettingDefinition(
                MonitoringSettingNames.AlertsEnabled,
                defaultValue: "false"),

            // Estado interno, no algo que nadie edite a mano. Se guarda para que un
            // reinicio o un despliegue no vuelva a avisar de lo que ya estaba avisado.
            new SettingDefinition(
                MonitoringSettingNames.AlertState,
                defaultValue: ""),

            // Tampoco lo edita nadie: lo escribe el notificador tras cada intento para
            // que la pagina pueda decir si el ultimo aviso llego de verdad.
            new SettingDefinition(
                MonitoringSettingNames.LastAlertResult,
                defaultValue: ""));

        // Marca por inquilino. No hace falta escribir NADA para que sean por inquilino: ABP
        // registra TenantSettingValueProvider entre el global y el de usuario, así que cada
        // uno lee lo suyo y, si no ha puesto nada, cae al valor del host.
        //
        // isVisibleToClients es lo que los mete en /api/abp/application-configuration, que
        // es de donde los saca el Angular. Los tres son públicos por naturaleza —el nombre y
        // el logo se ven en la pantalla de quien no ha entrado todavía—, así que exponerlos
        // no revela nada que no estuviera ya a la vista.
        context.Add(
            new SettingDefinition(
                BrandingSettingNames.AppName,
                defaultValue: null,
                isVisibleToClients: true),

            new SettingDefinition(
                BrandingSettingNames.AccentColor,
                defaultValue: BrandingConsts.DefaultAccentColor,
                isVisibleToClients: true),

            // Los BYTES del logo NO están aquí: AbpSettings.Value es varchar(2048) y un
            // data: URL de una imagen no cabe. Esto es solo la marca de versión; el archivo
            // vive en un BLOB. Ver BrandingSettingNames.
            new SettingDefinition(
                BrandingSettingNames.LogoVersion,
                defaultValue: "",
                isVisibleToClients: true));
    }
}
