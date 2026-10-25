using AuxiliarContable.Organizaciones;
using AuxiliarContable.ApiKeys;
using AuxiliarContable.Invitaciones;
using AuxiliarContable.Jobs;
using AuxiliarContable.Monitoring;
using AuxiliarContable.Notifications;
using AuxiliarContable.Security;
using AuxiliarContable.Telegram;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace AuxiliarContable.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class AuxiliarContableDbContext :
    AbpDbContext<AuxiliarContableDbContext>,
    ITenantManagementDbContext,
    IIdentityDbContext
{
    /* Add DbSet properties for your Aggregate Roots / Entities here. */

    public DbSet<Organizacion> Organizaciones { get; set; }

    public DbSet<Invitacion> Invitaciones { get; set; }

    public DbSet<ServerMetric> ServerMetrics { get; set; }

    public DbSet<RateLimitEvent> RateLimitEvents { get; set; }

    public DbSet<BlockedIp> BlockedIps { get; set; }

    public DbSet<ApiKey> ApiKeys { get; set; }

    public DbSet<TelegramLink> TelegramLinks { get; set; }

    // ⚠️ ESTOS DbSet NO SON DECORATIVOS. ABP aplica sus filtros globales (multi-tenancy y
    // soft delete) recorriendo las entidades que YA ESTAN en el modelo en el momento en que
    // se llama a base.OnModelCreating(), que aqui es la PRIMERA linea de OnModelCreating.
    // Una entidad registrada solo mas abajo con builder.Entity<X>() llega tarde a ese
    // recorrido y se queda SIN filtro: nada falla al compilar y las consultas devuelven las
    // filas de todos los tenants. Paso con Notificacion, y lo cazo un test de aislamiento.
    // Regla: toda entidad propia va tambien aqui.
    public DbSet<JobFailure> JobFailures { get; set; }

    public DbSet<Notificacion> Notifications { get; set; }

    public DbSet<NotificacionLeida> NotificationReads { get; set; }

    #region Entities from the modules

    /* Notice: We only implemented IIdentityProDbContext and ISaasDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityProDbContext and ISaasDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    // Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public AuxiliarContableDbContext(DbContextOptions<AuxiliarContableDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureTenantManagement();
        builder.ConfigureBlobStoring();

        /* Configure your own tables/entities inside here */


        // Organizaciones — AppOrganizaciones, con sus miembros como hijas del
        // agregado (se guardan y se borran con él).
        builder.Entity<Organizacion>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "Organizaciones", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Nombre).IsRequired().HasMaxLength(OrganizacionConsts.MaxNombreLength);
            b.Property(x => x.Descripcion).HasMaxLength(OrganizacionConsts.MaxDescripcionLength);
            // Sin índice único: con el borrado lógico chocaría con una borrada del mismo
            // nombre. El servicio comprueba el nombre entre las vivas.
            b.HasIndex(x => x.Nombre);
            b.HasMany(x => x.Miembros).WithOne().HasForeignKey(x => x.OrganizacionId).IsRequired();
        });

        builder.Entity<OrganizacionMiembro>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "OrganizacionMiembros", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(x => new { x.OrganizacionId, x.UserId });
            // «¿A qué organizaciones pertenezco?»: lo pregunta cada petición de un cliente.
            b.HasIndex(x => x.UserId);
        });

        // Invitaciones — tabla AppInvitaciones.
        builder.Entity<Invitacion>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "Invitaciones", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Email).IsRequired().HasMaxLength(InvitacionConsts.MaxEmailLength);
            b.Property(x => x.Rol).IsRequired().HasMaxLength(InvitacionConsts.MaxRolLength);
            b.Property(x => x.TokenHash).HasMaxLength(InvitacionConsts.TokenHashLength);
            // Abrir el enlace busca por hash. Único: dos invitaciones con el mismo enlace
            // serían un fallo del generador, y así revienta en vez de colarse.
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.Email);
            b.HasIndex(x => x.CreationTime);
        });


        // Monitoreo — tabla AppServerMetrics. Sin ISoftDelete ni IMultiTenant (ver la
        // entidad), asi que ConfigureByConvention no anade filtros globales aqui.
        builder.Entity<ServerMetric>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "ServerMetrics", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.HealthStatus).IsRequired()
                .HasMaxLength(MonitoringConsts.MaxHealthStatusLength);
            // Todas las consultas filtran por rango de Timestamp y agrupan por el: sin
            // este indice, cada carga de la pagina seria un recorrido completo de la tabla.
            b.HasIndex(x => x.Timestamp);
        });

        // Rate limiting — tabla AppRateLimitEvents. Igual que ServerMetric: sin
        // ISoftDelete ni IMultiTenant, la purga por retencion tiene que borrar de verdad.
        builder.Entity<RateLimitEvent>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "RateLimitEvents", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.IpAddress).IsRequired()
                .HasMaxLength(MonitoringConsts.MaxIpAddressLength);
            b.Property(x => x.Endpoint).IsRequired()
                .HasMaxLength(MonitoringConsts.MaxEndpointLength);
            // El panel filtra y la purga borra por WindowStartUtc.
            b.HasIndex(x => x.WindowStartUtc);
        });

        // IPs vetadas a mano desde el panel — tabla AppBlockedIps. A diferencia de
        // RateLimitEvent, esta SI es auditada (quien bloqueo y cuando): vetar por error
        // deja fuera a una persona y hay que poder reconstruir que paso.
        builder.Entity<BlockedIp>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "BlockedIps", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.IpAddress).IsRequired()
                .HasMaxLength(SecurityConsts.MaxIpAddressLength);
            b.Property(x => x.Reason).IsRequired()
                .HasMaxLength(SecurityConsts.MaxBlockReasonLength);
            // UNICO: una IP se bloquea una vez. Sin esto, dos filas para la misma
            // direccion harian ambiguo cual caduca y cual manda.
            b.HasIndex(x => x.IpAddress).IsUnique();
        });

        // Claves de API — tabla AppApiKeys. Solo el HASH, nunca la clave.
        builder.Entity<ApiKey>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "ApiKeys", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(ApiKeyConsts.MaxNameLength);
            b.Property(x => x.KeyHash).IsRequired().HasMaxLength(ApiKeyConsts.HashLength);
            b.Property(x => x.Pista).IsRequired().HasMaxLength(ApiKeyConsts.PistaLength);
            // UNICO e indexado: es la consulta del camino de autenticacion, en cada
            // peticion que traiga clave. Y unico porque dos filas con el mismo hash serian
            // la misma clave autenticando como dos usuarios distintos.
            b.HasIndex(x => x.KeyHash).IsUnique();
            // El panel lista las del usuario actual.
            b.HasIndex(x => x.UserId);
        });

        // Vinculo de un usuario con su chat de Telegram.
        builder.Entity<TelegramLink>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "TelegramLinks", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.TokenHash).HasMaxLength(TelegramLinkConsts.HashLength);
            b.Property(x => x.TelegramUserName).HasMaxLength(64);
            // UNICO: un usuario tiene como mucho un vinculo. Sin esto, dos filas para el
            // mismo usuario harian ambiguo a que chat mandar su enlace de recuperacion.
            b.HasIndex(x => x.UserId).IsUnique();
            // La confirmacion busca por aqui, y ocurre en cada mensaje que recibe el bot.
            b.HasIndex(x => x.TokenHash);
        });

        // Intentos fallidos de un trabajo en segundo plano — tabla AppJobFailures.
        // Complementa a AbpBackgroundJobs, que cuenta los intentos pero NO guarda el
        // motivo (verificado en refs/abp). Sin ISoftDelete: la purga borra de verdad.
        builder.Entity<JobFailure>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "JobFailures", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.JobName).IsRequired().HasMaxLength(JobConsts.MaxJobNameLength);
            b.Property(x => x.ExceptionType).IsRequired()
                .HasMaxLength(JobConsts.MaxExceptionTypeLength);
            b.Property(x => x.Message).IsRequired().HasMaxLength(JobConsts.MaxMessageLength);
            b.Property(x => x.StackTrace).HasMaxLength(JobConsts.MaxStackTraceLength);
            // El panel busca los fallos de los trabajos que esta pintando, y la purga borra
            // por fecha. Es un indice compuesto porque las dos consultas van juntas: la del
            // panel pide un JobId y se queda con el AttemptedUtc mas alto.
            b.HasIndex(x => new { x.JobId, x.AttemptedUtc });
            b.HasIndex(x => x.AttemptedUtc);
        });

        // Avisos dentro de la aplicacion — tabla AppNotifications. ABP open source no trae
        // sistema de notificaciones (es de Pro). SI es multi-tenant: un aviso de un tenant
        // no es de otro. Sin ISoftDelete: la purga borra de verdad.
        builder.Entity<Notificacion>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "Notifications", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Title).IsRequired().HasMaxLength(NotificationConsts.MaxTitleLength);
            b.Property(x => x.Body).IsRequired().HasMaxLength(NotificationConsts.MaxBodyLength);
            b.Property(x => x.Url).HasMaxLength(NotificationConsts.MaxUrlLength);
            b.Property(x => x.RequiredPermission)
                .HasMaxLength(NotificationConsts.MaxPermissionLength);
            // Las dos formas de llegar a un aviso: por destinatario personal o por permiso.
            // Ambas ordenan siempre por fecha descendente.
            b.HasIndex(x => new { x.UserId, x.CreationTime });
            b.HasIndex(x => new { x.RequiredPermission, x.CreationTime });
            // La purga por retencion.
            b.HasIndex(x => x.CreationTime);
        });

        // Quien ha leido que — tabla AppNotificationReads. Una fila por (aviso, persona), y
        // solo cuando se lee: "no leido" es la AUSENCIA de fila. Ver la entidad.
        builder.Entity<NotificacionLeida>(b =>
        {
            b.ToTable(
                AuxiliarContableConsts.DbTablePrefix + "NotificationReads", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            // UNICO: leer dos veces no puede dejar dos filas. Ademas es el indice por el que
            // se pregunta "esta leida?" en cada aviso de la lista.
            b.HasIndex(x => new { x.NotificacionId, x.UserId }).IsUnique();
            // Que al borrar un aviso caduco se lleve por delante sus marcas de lectura lo
            // hace la propia base: si no, quedarian filas huerfanas para siempre.
            b.HasOne<Notificacion>()
                .WithMany()
                .HasForeignKey(x => x.NotificacionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
