using IgasConsole.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.FeatureManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Volo.Abp.Localization;

namespace IgasConsole.Permissions;

/// <summary>
/// Permisos propios de la aplicación. Quién recibe cada uno lo decide
/// <c>IgasRolesDataSeedContributor</c>; qué usuarios y roles puede tocar cada persona lo
/// decide la jerarquía (<c>JerarquiaDeUsuarios</c>), que va aparte de los permisos.
///
/// <para>Hay dos familias. Las <b>de negocio</b> (Análisis, marca) son para el Owner y su
/// gente. Las <b>técnicas</b> (métricas, trabajos, seguridad, auditoría, explorador de BD)
/// quedan solo para el Developer: además de no aportarle nada al negocio, casi todas
/// enseñan actividad por usuario — y eso incluye la del Developer, que el Owner no debe
/// ver.</para>
/// </summary>
public class IgasConsolePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(IgasConsolePermissions.GroupName, L("Permission:IgasConsole"));

        // --- Negocio -------------------------------------------------------------------

        myGroup.AddPermission(IgasConsolePermissions.Organizaciones.Default, L("Permission:Organizaciones"));

        myGroup.AddPermission(IgasConsolePermissions.Actividad.Default, L("Permission:Actividad"));

        myGroup.AddPermission(IgasConsolePermissions.Branding.Default, L("Permission:Branding"));

        // --- Técnicos (Developer) --------------------------------------------------------

        myGroup.AddPermission(IgasConsolePermissions.ApiKeys.Default, L("Permission:ApiKeys"));

        var metrics = myGroup.AddPermission(IgasConsolePermissions.Metrics.Default, L("Permission:Metrics"));
        metrics.AddChild(IgasConsolePermissions.Metrics.ManageSettings, L("Permission:Metrics.ManageSettings"));

        var seguridad = myGroup.AddPermission(IgasConsolePermissions.Security.BlockedIps, L("Permission:Security.BlockedIps"));
        seguridad.AddChild(IgasConsolePermissions.Security.ManageBlockedIps, L("Permission:Security.BlockedIps.Manage"));

        myGroup.AddPermission(IgasConsolePermissions.Security.Logs, L("Permission:Security.Logs"));

        myGroup.AddPermission(IgasConsolePermissions.AuditLogs.Default, L("Permission:AuditLogs"));

        var trabajos = myGroup.AddPermission(IgasConsolePermissions.Jobs.Default, L("Permission:Jobs"));
        trabajos.AddChild(IgasConsolePermissions.Jobs.Manage, L("Permission:Jobs.Manage"));

        myGroup.AddPermission(IgasConsolePermissions.SystemStatus.Default, L("Permission:SystemStatus"));

        var dbExplorer = myGroup.AddPermission(IgasConsolePermissions.DbExplorer.Default, L("Permission:DbExplorer"));
        dbExplorer.AddChild(IgasConsolePermissions.DbExplorer.Edit, L("Permission:DbExplorer.Edit"));
    }

    /// <summary>
    /// Apaga los permisos de módulos de ABP que aquí no significan nada, para que la
    /// pantalla de permisos del Owner solo enseñe lo que de verdad hace algo:
    /// <list type="bullet">
    /// <item>Inquilinos y características: es una sola organización, sin features.</item>
    /// <item>Ajustes de ABP (correo SMTP y zona horaria): el correo sale por Brevo y se
    /// configura en el servidor; esas pestañas cambiarían valores que nadie usa. La pantalla
    /// «Configuraciones» sigue para las reglas de contraseña, que cuelgan de Identity.</item>
    /// </list>
    /// <para>Un permiso apagado no se concede a nadie —ni al Developer— y ABP lo quita de la
    /// pantalla de permisos (verificado en <c>refs/abp</c>: <c>PermissionChecker</c> y
    /// <c>PermissionAppService.GetAsync</c> filtran por <c>IsEnabled</c>). En PostDefine
    /// porque esos módulos definen los suyos antes o después según el orden de carga.</para>
    /// </summary>
    public override void PostDefine(IPermissionDefinitionContext context)
    {
        foreach (var grupo in new[]
                 {
                     TenantManagementPermissions.GroupName,
                     FeatureManagementPermissions.GroupName,
                     SettingManagementPermissions.GroupName,
                 })
        {
            foreach (var permiso in context.GetGroupOrNull(grupo)?.GetPermissionsWithChildren() ?? [])
            {
                permiso.IsEnabled = false;
            }
        }
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<IgasConsoleResource>(name);
    }
}
