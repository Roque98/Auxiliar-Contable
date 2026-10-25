using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AuxiliarContable.Localization;
using AuxiliarContable.MultiTenancy;
using System;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.SettingManagement;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.Caching;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement.OpenIddict;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Emailing;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.TenantManagement;
using Volo.Abp.Timing;

namespace AuxiliarContable;

[DependsOn(
    typeof(AuxiliarContableDomainSharedModule),
    typeof(AbpAuditLoggingDomainModule),
    typeof(AbpCachingModule),
    typeof(AbpBackgroundJobsDomainModule),
    typeof(AbpFeatureManagementDomainModule),
    typeof(AbpPermissionManagementDomainIdentityModule),
    typeof(AbpPermissionManagementDomainOpenIddictModule),
    typeof(AbpSettingManagementDomainModule),
    typeof(AbpEmailingModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpOpenIddictDomainModule),
    typeof(AbpTenantManagementDomainModule),
    typeof(BlobStoringDatabaseDomainModule)    )]
public class AuxiliarContableDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpMultiTenancyOptions>(options =>
        {
            options.IsEnabled = MultiTenancyConsts.IsEnabled;
        });

        // OBLIGATORIO con PostgreSQL. EF Core mapea DateTime a 'timestamp with time zone',
        // y Npgsql rechaza cualquier DateTime que no sea Kind=Utc. Por defecto AbpClockOptions
        // usa Unspecified, con lo que IClock.Now devuelve DateTime.Now (Kind=Local) y TODA
        // escritura auditada (CreationTime, LastModificationTime...) revienta al guardar.
        // Con Utc, IClock.Now pasa a DateTime.UtcNow y Clock.Normalize convierte a UTC.
        Configure<AbpClockOptions>(options =>
        {
            options.Kind = DateTimeKind.Utc;
        });

        // Almacenamiento de archivos (BLOBs). El proveedor va EXPLÍCITO aunque
        // BlobStoringDatabaseDomainModule elija la base de datos por su cuenta cuando nadie
        // ha configurado nada: ese automatismo solo actúa "si no hay proveedor elegido", así
        // que el día que se añada otro proveedor (S3, disco...) dejaría de aplicarse en
        // silencio y las fotos se irían a otro sitio sin que nada avise.
        //
        // Sin Docker y con un solo servidor, la base de datos es el destino correcto: no hay
        // volumen compartido que montar y las fotos entran en la copia de seguridad de la
        // base junto con todo lo demás. Si algún día hay más de un servidor de aplicación,
        // esto es lo único que habría que cambiar (ver ProfilePictureContainer).
        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.ConfigureDefault(container =>
            {
                container.UseDatabase();
            });
        });

        // Siempre, no solo en DEBUG: el DbMigrator y los tests cargan este módulo y no deben
        // mandar correo. El que envía de verdad (BrevoEmailSender) lo pone el host.
        context.Services.Replace(ServiceDescriptor.Singleton<IEmailSender, NullEmailSender>());
    }
}
