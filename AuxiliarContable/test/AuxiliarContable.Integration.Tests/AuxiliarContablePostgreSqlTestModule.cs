using AuxiliarContable.Data;
using AuxiliarContable.DataSources;
using AuxiliarContable.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AuxiliarContable.DbExplorer;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;

namespace AuxiliarContable;

/// <summary>
/// Módulo de arranque para los tests de INTEGRACIÓN contra el motor real
/// (PostgreSQL local, sin Docker). Reemplaza al módulo SQLite en memoria.
/// </summary>
[DependsOn(
    typeof(AuxiliarContableApplicationTestModule),
    typeof(AuxiliarContableEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class AuxiliarContablePostgreSqlTestModule : AbpModule
{
    /// <summary>
    /// Los proveedores de códigos «Email» y «Phone» de ABP los registra el módulo web de
    /// Identity (AbpIdentityAspNetCoreModule), que estos tests no cargan. Se registran aquí
    /// los mismos, para probar con los códigos reales (de un solo uso y con caducidad).
    /// </summary>
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<Microsoft.AspNetCore.Identity.IdentityBuilder>(builder => builder
            .AddTokenProvider<Volo.Abp.Identity.AspNetCore.AbpEmailTwoFactorTokenProvider>(
                Microsoft.AspNetCore.Identity.TokenOptions.DefaultEmailProvider)
            .AddTokenProvider<Volo.Abp.Identity.AspNetCore.AbpPhoneNumberTwoFactorTokenProvider>(
                Microsoft.AspNetCore.Identity.TokenOptions.DefaultPhoneProvider));
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddDataProtection();

        // Habilita el explorador de BD en los tests (en el Host solo va en Development).
        Configure<DbExplorerOptions>(options => options.IsEnabled = true);

        // Se ENCOLAN trabajos pero no se ejecutan. Dos motivos:
        // 1. Las implementaciones viven en HttpApi.Host, que estos tests no cargan: el
        //    worker no encontraria quien ejecuta y llenaria el log de errores.
        // 2. Interesa poder mirar la fila tal cual queda en la tabla — sobre todo QUE NO
        //    LLEVE SECRETOS —, y un worker ejecutando la borraria a media prueba.
        Configure<AbpBackgroundJobOptions>(options => options.IsJobExecutionEnabled = false);

        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });

        // El host dice si Brevo está configurado; aquí se finge que sí para poder invitar
        // (el IEmailSender de los tests no manda nada). Un test puede apagarlo.
        context.Services.Replace(ServiceDescriptor.Singleton<AuxiliarContable.Notifications.IEstadoDelCorreo, CorreoDePrueba>());
        // Los códigos de verificación no salen: se quedan aquí para que el test los lea.
        context.Services.Replace(ServiceDescriptor.Singleton<AuxiliarContable.Seguridad.IEnviadorDeCodigos, EnviadorDePrueba>());
        // La autorización está en «todo permitido»: sin esto nadie sería cliente y no se
        // podría probar quién ve qué clientes. Ver AlcanceSegunRol.
        context.Services.Replace(ServiceDescriptor.Transient<AuxiliarContable.Organizaciones.AlcanceDeOrganizaciones, AlcanceSegunRol>());
        context.Services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = PostgresTestConsts.MainConnectionString;
        });

        context.Services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(ctx => ctx.UseNpgsql());
        });
    }
}

public class CorreoDePrueba : AuxiliarContable.Notifications.IEstadoDelCorreo
{
    public bool PuedeEnviar { get; set; } = true;
}

public class EnviadorDePrueba : AuxiliarContable.Seguridad.IEnviadorDeCodigos
{
    public bool CorreoDisponible { get; set; } = true;

    public bool SmsDisponible { get; set; } = true;

    public string? UltimoDestino { get; private set; }

    public string? UltimoCodigo { get; private set; }

    public System.Threading.Tasks.Task EnviarPorCorreoAsync(string correo, string codigo, AuxiliarContable.Seguridad.PropositoDeCodigo proposito)
    {
        UltimoDestino = correo;
        UltimoCodigo = codigo;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task EnviarPorSmsAsync(string telefono, string codigo, AuxiliarContable.Seguridad.PropositoDeCodigo proposito)
    {
        UltimoDestino = telefono;
        UltimoCodigo = codigo;
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

/// <summary>
/// «¿Ve todos los clientes?» según el rol de la sesión, como los permisos con que nacen los
/// roles: el Usuario es cliente (no tiene Organizaciones) y el resto del equipo sí.
/// </summary>
public class AlcanceSegunRol : AuxiliarContable.Organizaciones.AlcanceDeOrganizaciones
{
    private readonly Volo.Abp.Users.ICurrentUser _actual;

    public AlcanceSegunRol(
        Volo.Abp.Authorization.Permissions.IPermissionChecker permisos,
        Volo.Abp.Domain.Repositories.IRepository<AuxiliarContable.Organizaciones.Organizacion, System.Guid> organizaciones,
        Volo.Abp.Linq.IAsyncQueryableExecuter ejecutor,
        Volo.Abp.Users.ICurrentUser actual) : base(permisos, organizaciones, ejecutor)
    {
        _actual = actual;
    }

    public override System.Threading.Tasks.Task<bool> VeTodasAsync() =>
        System.Threading.Tasks.Task.FromResult(!_actual.IsInRole(AuxiliarContable.Identity.AuxiliarRoles.Usuario));
}
