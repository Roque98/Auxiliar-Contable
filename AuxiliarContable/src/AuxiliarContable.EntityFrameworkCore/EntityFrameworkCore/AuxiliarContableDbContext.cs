using AuxiliarContable.Conversaciones;
using AuxiliarContable.Documentos;
using AuxiliarContable.PerfilesFiscales;
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

    public DbSet<Privacidad.ConsentimientoDePrivacidad> ConsentimientosDePrivacidad { get; set; }

    public DbSet<Documento> Documentos { get; set; }

    public DbSet<PerfilFiscal> PerfilesFiscales { get; set; }

    public DbSet<Contabilidad.PeriodoContable> PeriodosContables { get; set; }
    public DbSet<Contabilidad.MesSinEntrega> MesesSinEntrega { get; set; }
    public DbSet<Contabilidad.ImpuestosDelMes> ImpuestosDeMeses { get; set; }
    public DbSet<Contabilidad.SolicitudDeDescargaMasiva> SolicitudesDeDescargaMasiva { get; set; }

    public DbSet<Contabilidad.ConciliacionBancaria> ConciliacionesBancarias { get; set; }

    public DbSet<Contabilidad.PartidaPendiente> PartidasPendientes { get; set; }

    public DbSet<Contabilidad.MovimientoFueraDelBanco> MovimientosFueraDelBanco { get; set; }

    public DbSet<Contabilidad.EvidenciaDeOperacion> EvidenciasDeOperaciones { get; set; }

    public DbSet<Facturacion.SolicitudDeFactura> SolicitudesDeFactura { get; set; }

    public DbSet<Contabilidad.Operacion> Operaciones { get; set; }

    public DbSet<Contabilidad.PolizasDelMes> PolizasDeMeses { get; set; }

    public DbSet<Contabilidad.ContabilidadDelMes> ContabilidadesDeMeses { get; set; }
    public DbSet<Contabilidad.OperacionInterpretada> OperacionesInterpretadas { get; set; }
    public DbSet<Normativa.AnalisisNormativo> AnalisisNormativos { get; set; }
    public DbSet<Contabilidad.ConciliacionIntegral> ConciliacionesIntegrales { get; set; }
    public DbSet<Nomina.Trabajador> Trabajadores { get; set; }
    public DbSet<Nomina.PeriodoDeNomina> PeriodosDeNomina { get; set; }
    public DbSet<Nomina.MovimientoAfiliatorio> MovimientosAfiliatorios { get; set; }
    public DbSet<Nomina.ContratoEspecializado> ContratosEspecializados { get; set; }
    public DbSet<Nomina.CumplimientoRepse> CumplimientosRepse { get; set; }

    public DbSet<Concentrado.PagoDeObligacion> PagosDeObligaciones { get; set; }

    public DbSet<Contabilidad.SaldosDeApertura> SaldosDeAperturas { get; set; }

    public DbSet<Credenciales.CredencialesDelCliente> CredencialesDeClientes { get; set; }

    public DbSet<Credenciales.NipDeUsuario> NipsDeUsuarios { get; set; }

    public DbSet<Credenciales.UsoDeCredencial> UsosDeCredenciales { get; set; }

    public DbSet<DatoDelPerfil> DatosDePerfiles { get; set; }

    public DbSet<Conversacion> Conversaciones { get; set; }

    public DbSet<MensajeDeConversacion> MensajesDeConversaciones { get; set; }

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

        // Documentos de los clientes — tabla AppDocumentos. El CONTENIDO no va aqui: va cifrado
        // al disco (AlmacenDeDocumentos). Aqui solo lo necesario para buscarlo y deduplicar.
        builder.Entity<Documento>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "Documentos", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.NombreOriginal).IsRequired().HasMaxLength(DocumentoConsts.MaxNombreLength);
            b.Property(x => x.TipoMime).IsRequired().HasMaxLength(DocumentoConsts.MaxTipoMimeLength);
            b.Property(x => x.Sha256).IsRequired().HasMaxLength(DocumentoConsts.HashLength);
            b.Property(x => x.Motivo).HasMaxLength(500);
            b.Property(x => x.Analisis).HasColumnType("text");
            // Duplicados: el mismo archivo para el mismo cliente. NO unico a proposito: con el
            // borrado logico, un documento borrado y vuelto a subir chocaria con su fantasma.
            b.HasIndex(x => new { x.OrganizacionId, x.Sha256 });
            b.HasIndex(x => new { x.OrganizacionId, x.Tipo });
            b.Property(x => x.Uuid).HasMaxLength(DocumentoConsts.UuidLength);
            b.Property(x => x.EstatusSat).HasMaxLength(DocumentoConsts.MaxEstatusSatLength);
            // Por UUID: lo que baja el Descargador 1 solo entra si el cliente no lo tenía.
            b.HasIndex(x => new { x.OrganizacionId, x.Uuid });
        });

        // AppImpuestosDelMes: el pago provisional al día de cada mes (JSON del motor sin el Excel).
        builder.Entity<Contabilidad.ImpuestosDelMes>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "ImpuestosDelMes", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Resultado).HasColumnType("text");
            b.Property(x => x.Error).HasMaxLength(Contabilidad.ImpuestosDelMes.MaxErrorLength);
            b.Property(x => x.HuellaDelPerfil).HasMaxLength(Contabilidad.ImpuestosDelMes.MaxHuellaLength);
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes }).IsUnique();
            b.HasIndex(x => new { x.PorActualizar, x.PedidoUtc });
        });

        // AppSolicitudesDeDescargaMasiva: cada solicitud al servicio de Descarga Masiva del SAT y su avance.
        builder.Entity<Contabilidad.SolicitudDeDescargaMasiva>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "SolicitudesDeDescargaMasiva", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.IdSolicitud).HasMaxLength(Contabilidad.SolicitudDeDescargaMasiva.MaxIdLength);
            b.Property(x => x.Mensaje).HasMaxLength(Contabilidad.SolicitudDeDescargaMasiva.MaxMensajeLength);
            b.Property(x => x.Paquetes).HasMaxLength(Contabilidad.SolicitudDeDescargaMasiva.MaxPaquetesLength);
            // Hora de México tal cual (así la entiende el SAT), sin zona.
            b.Property(x => x.Desde).HasColumnType("timestamp without time zone");
            b.Property(x => x.Hasta).HasColumnType("timestamp without time zone");
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes });
            b.HasIndex(x => new { x.Estado, x.RevisadaUtc });
        });

        // AppMesesSinEntrega: el cliente no entregó nada ese mes y se trabaja solo con lo que se baja del SAT.
        builder.Entity<Contabilidad.MesSinEntrega>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "MesesSinEntrega", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes }).IsUnique();
        });

        // Cierre mensual — un mes de un cliente es un periodo que se concilia y se cierra.
        // AppPeriodosContables: solo los que alguna vez se cerraron (sin fila = abierto).
        builder.Entity<Contabilidad.PeriodoContable>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "PeriodosContables", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes }).IsUnique();
        });

        // AppConciliacionesBancarias: la ultima de cada mes, con el resultado del motor tal cual.
        // Texto y no jsonb: se guarda y se devuelve entero, nunca se consulta por dentro.
        builder.Entity<Contabilidad.ConciliacionBancaria>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "ConciliacionesBancarias", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Rfc).IsRequired().HasMaxLength(13);
            b.Property(x => x.Huella).IsRequired().HasMaxLength(Contabilidad.ContabilidadConsts.HuellaLength);
            b.Property(x => x.Resultado).IsRequired();
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes }).IsUnique();
        });

        // AppPartidasPendientes: lo decidido de cada movimiento sin factura, por su clave, para que
        // sobreviva a una nueva conciliacion.
        builder.Entity<Contabilidad.PartidaPendiente>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "PartidasPendientes", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Clave).IsRequired().HasMaxLength(Contabilidad.ContabilidadConsts.ClaveLength);
            b.Property(x => x.Descripcion).IsRequired().HasMaxLength(Contabilidad.ContabilidadConsts.MaxDescripcionLength);
            b.Property(x => x.Nota).HasMaxLength(Contabilidad.ContabilidadConsts.MaxNotaLength);
            b.Property(x => x.Respuesta).HasMaxLength(Contabilidad.ContabilidadConsts.MaxRespuestaLength);
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes, x.Clave }).IsUnique();
        });

        // Dinero que no pasa por el banco: efectivo (y cortes de caja) y lo comprado o vendido a credito.
        builder.Entity<Contabilidad.MovimientoFueraDelBanco>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "MovimientosFueraDelBanco", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Fecha).HasColumnType("date");
            b.Property(x => x.Vence).HasColumnType("date");
            b.Property(x => x.Saldado).HasColumnType("date");
            b.Property(x => x.Importe).HasPrecision(18, 2);
            b.Property(x => x.Concepto).IsRequired().HasMaxLength(Contabilidad.DineroFueraDelBancoConsts.MaxConceptoLength);
            b.Property(x => x.Contraparte).HasMaxLength(Contabilidad.DineroFueraDelBancoConsts.MaxContraparteLength);
            b.Property(x => x.Nota).HasMaxLength(Contabilidad.DineroFueraDelBancoConsts.MaxNotaLength);
            b.HasIndex(x => new { x.OrganizacionId, x.Fecha });
        });

        // Control interno: la evidencia de cada operacion (banco por su clave, o efectivo y credito por su id).
        builder.Entity<Contabilidad.EvidenciaDeOperacion>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "EvidenciasDeOperaciones", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Clave).HasMaxLength(Contabilidad.ContabilidadConsts.ClaveLength);
            b.Property(x => x.Nota).HasMaxLength(Contabilidad.ControlInternoConsts.MaxNotaLength);
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes });
        });

        // Saldos de apertura: la balanza con la que llega el cliente (uno por cliente).
        builder.Entity<Contabilidad.SaldosDeApertura>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "SaldosDeApertura", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Saldos).IsRequired().HasColumnType("text");
            b.HasIndex(x => x.OrganizacionId).IsUnique();
        });

        // Pólizas del mes: lo último que se contabilizó (JSON del motor).
        builder.Entity<Contabilidad.PolizasDelMes>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "PolizasDeMeses", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Resultado).IsRequired().HasColumnType("text");
            b.Property(x => x.DatosDeCierre).HasColumnType("text");
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes }).IsUnique();
        });

        // Nómina: los trabajadores de cada cliente y sus periodos (incidencias y cálculo del motor).
        builder.Entity<Nomina.Trabajador>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "Trabajadores", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Nombre).IsRequired().HasMaxLength(Nomina.Trabajador.MaxNombreLength);
            b.Property(x => x.Rfc).HasMaxLength(13);
            b.Property(x => x.Curp).HasMaxLength(18);
            b.Property(x => x.Nss).HasMaxLength(11);
            b.Property(x => x.SalarioDiario).HasPrecision(18, 2);
            b.Property(x => x.SalarioDiarioIntegrado).HasPrecision(18, 2);
            b.Property(x => x.PrimaVacacionalPct).HasPrecision(6, 2);
            b.Property(x => x.CreditoInfonavitTipo).HasMaxLength(20);
            b.Property(x => x.CreditoInfonavitValor).HasPrecision(18, 4);
            b.Property(x => x.CreditoInfonavitNumero).HasMaxLength(10);
            b.Property(x => x.Fonacot).HasPrecision(18, 2);
            b.Property(x => x.PrimerApellido).HasMaxLength(Nomina.Trabajador.MaxNombreLength);
            b.Property(x => x.SegundoApellido).HasMaxLength(Nomina.Trabajador.MaxNombreLength);
            b.Property(x => x.CodigoPostal).HasMaxLength(5);
            b.Property(x => x.NumeroEmpleado).HasMaxLength(10);
            b.Property(x => x.Umf).HasMaxLength(3);
            b.HasIndex(x => x.OrganizacionId);
        });
        builder.Entity<Nomina.MovimientoAfiliatorio>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "MovimientosAfiliatorios", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Tipo).IsRequired().HasMaxLength(20);
            b.Property(x => x.SalarioDiario).HasPrecision(18, 2);
            b.Property(x => x.SalarioDiarioIntegrado).HasPrecision(18, 2);
            b.Property(x => x.CausaBaja).HasMaxLength(1);
            b.HasIndex(x => new { x.OrganizacionId, x.PresentadoUtc });
        });
        builder.Entity<Nomina.ContratoEspecializado>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "ContratosEspecializados", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Rol).IsRequired().HasMaxLength(10);
            b.Property(x => x.ContraparteRfc).IsRequired().HasMaxLength(13);
            b.Property(x => x.ContraparteNombre).IsRequired().HasMaxLength(Nomina.ContratoEspecializado.MaxNombreLength);
            b.Property(x => x.Objeto).IsRequired().HasMaxLength(Nomina.ContratoEspecializado.MaxObjetoLength);
            b.Property(x => x.RegistroRepse).HasMaxLength(30);
            b.Property(x => x.Trabajadores).HasColumnType("text");
            b.HasIndex(x => x.OrganizacionId);
        });
        builder.Entity<Nomina.CumplimientoRepse>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "CumplimientosRepse", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Clave).IsRequired().HasMaxLength(20);
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Periodo });
        });
        builder.Entity<Concentrado.PagoDeObligacion>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "PagosDeObligaciones", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Clave).IsRequired().HasMaxLength(Concentrado.PagoDeObligacion.MaxClaveLength);
            b.Property(x => x.Nota).HasMaxLength(Concentrado.PagoDeObligacion.MaxNotaLength);
            b.Property(x => x.Importe).HasPrecision(18, 2);
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes });
        });
        builder.Entity<Nomina.PeriodoDeNomina>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "PeriodosDeNomina", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Periodicidad).IsRequired().HasMaxLength(20);
            b.Property(x => x.Incidencias).HasColumnType("text");
            b.Property(x => x.Resultado).HasColumnType("text");
            b.HasIndex(x => new { x.OrganizacionId, x.Inicio });
        });

        // AppOperacionesInterpretadas: lo que Claude interpretó de cada operación (una por operación).
        builder.Entity<Contabilidad.OperacionInterpretada>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "OperacionesInterpretadas", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Huella).HasMaxLength(Contabilidad.OperacionInterpretada.HuellaLength);
            b.Property(x => x.Resultado).HasColumnType("text");
            b.Property(x => x.Error).HasMaxLength(Contabilidad.OperacionInterpretada.MaxErrorLength);
            b.HasIndex(x => x.OperacionId).IsUnique();
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes });
            b.HasIndex(x => new { x.Pedida, x.PedidaUtc });
        });

        // AppConciliacionesIntegrales: cada vuelta del botón «Conciliar» (pasos y resultado por mes, en JSON).
        builder.Entity<Contabilidad.ConciliacionIntegral>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "ConciliacionesIntegrales", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Pasos).IsRequired().HasColumnType("text");
            b.Property(x => x.Resultado).HasColumnType("text");
            b.Property(x => x.Error).HasMaxLength(Contabilidad.ConciliacionIntegral.MaxErrorLength);
            b.HasIndex(x => new { x.OrganizacionId, x.PedidaUtc });
            b.HasIndex(x => new { x.Estado, x.PedidaUtc });
        });

        // AppAnalisisNormativos: hipótesis normativa de cada cliente (pregunta, conclusión y el JSON del motor).
        builder.Entity<Normativa.AnalisisNormativo>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "AnalisisNormativos", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Pregunta).IsRequired().HasMaxLength(Normativa.AnalisisNormativo.MaxPreguntaLength);
            b.Property(x => x.ContraparteTipoDePersona).HasMaxLength(Normativa.AnalisisNormativo.MaxCortoLength);
            b.Property(x => x.ContraparteResidencia).HasMaxLength(Normativa.AnalisisNormativo.MaxCortoLength);
            b.Property(x => x.Disparadores).HasMaxLength(Normativa.AnalisisNormativo.MaxDisparadoresLength);
            b.Property(x => x.Resultado).HasColumnType("text");
            b.Property(x => x.Conclusion).HasMaxLength(Normativa.AnalisisNormativo.MaxConclusionLength);
            b.Property(x => x.Confianza).HasMaxLength(Normativa.AnalisisNormativo.MaxCortoLength);
            b.Property(x => x.Error).HasMaxLength(Normativa.AnalisisNormativo.MaxErrorLength);
            b.HasIndex(x => new { x.OrganizacionId, x.CreationTime });
            b.HasIndex(x => new { x.Estado, x.PedidoUtc });
        });

        // AppContabilidadesDeMeses: la contabilidad al día de cada mes (por actualizar, qué se hizo la última vez).
        builder.Entity<Contabilidad.ContabilidadDelMes>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "ContabilidadesDeMeses", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Resumen).HasMaxLength(Contabilidad.ContabilidadDelMes.MaxTextoLength);
            b.Property(x => x.Error).HasMaxLength(Contabilidad.ContabilidadDelMes.MaxTextoLength);
            b.HasIndex(x => new { x.OrganizacionId, x.Anio, x.Mes }).IsUnique();
            b.HasIndex(x => new { x.PorActualizar, x.PedidoUtc });
        });

        // Operaciones: factura · transacción · control interno de cada compra, gasto o venta.
        builder.Entity<Contabilidad.Operacion>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "Operaciones", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Importe).HasPrecision(18, 2);
            b.Property(x => x.Descripcion).IsRequired().HasMaxLength(Contabilidad.OperacionConsts.MaxDescripcionLength);
            b.Property(x => x.Contraparte).HasMaxLength(Contabilidad.OperacionConsts.MaxContraparteLength);
            b.Property(x => x.Uuid).HasMaxLength(Contabilidad.OperacionConsts.UuidLength);
            b.Property(x => x.Referencia).HasMaxLength(Contabilidad.OperacionConsts.MaxReferenciaLength);
            b.Property(x => x.ClaveDeMovimiento).HasMaxLength(Contabilidad.ContabilidadConsts.ClaveLength);
            b.Property(x => x.Aceptados).HasMaxLength(Contabilidad.OperacionConsts.MaxAceptadosLength);
            b.Ignore(x => x.CodigosAceptados);
            b.Property(x => x.FacturaMotivo).HasMaxLength(Contabilidad.OperacionConsts.MaxMotivoLength);
            b.Property(x => x.TransaccionMotivo).HasMaxLength(Contabilidad.OperacionConsts.MaxMotivoLength);
            b.Property(x => x.ControlMotivo).HasMaxLength(Contabilidad.OperacionConsts.MaxMotivoLength);
            b.Ignore(x => x.Ligadas);
            b.Ignore(x => x.Respondidas);
            b.HasIndex(x => new { x.OrganizacionId, x.Fecha });
            b.HasIndex(x => new { x.OrganizacionId, x.Uuid });
            b.HasIndex(x => new { x.OrganizacionId, x.ClaveDeMovimiento });
        });

        // Solicitudes de factura: lo que el cliente pide que se le timbre; conceptos en JSON (texto).
        builder.Entity<Facturacion.SolicitudDeFactura>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "SolicitudesDeFactura", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ReceptorRfc).IsRequired().HasMaxLength(Facturacion.FacturacionConsts.RfcLength);
            b.Property(x => x.ReceptorNombre).IsRequired().HasMaxLength(Facturacion.FacturacionConsts.MaxNombreLength);
            b.Property(x => x.ReceptorCodigoPostal).IsRequired().HasMaxLength(Facturacion.FacturacionConsts.CodigoPostalLength);
            b.Property(x => x.ReceptorRegimen).IsRequired().HasMaxLength(Facturacion.FacturacionConsts.ClaveCortaLength);
            b.Property(x => x.UsoCfdi).IsRequired().HasMaxLength(Facturacion.FacturacionConsts.ClaveCortaLength);
            b.Property(x => x.FormaDePago).IsRequired().HasMaxLength(Facturacion.FacturacionConsts.ClaveCortaLength);
            b.Property(x => x.MetodoDePago).IsRequired().HasMaxLength(Facturacion.FacturacionConsts.ClaveCortaLength);
            b.Property(x => x.Conceptos).IsRequired();
            b.Property(x => x.Subtotal).HasPrecision(18, 2);
            b.Property(x => x.Iva).HasPrecision(18, 2);
            b.Property(x => x.Total).HasPrecision(18, 2);
            b.Property(x => x.NotaDelCliente).HasMaxLength(Facturacion.FacturacionConsts.MaxNotaLength);
            b.Property(x => x.Respuesta).HasMaxLength(Facturacion.FacturacionConsts.MaxNotaLength);
            b.HasIndex(x => new { x.OrganizacionId, x.Estado });
            b.HasIndex(x => x.Estado);
        });

        // Credenciales del cliente (e.firma y CIEC): lo secreto va cifrado con su propia llave
        // (CifradorDeCredenciales); RFC, numero de certificado y vigencia en claro para avisar sin descifrar.
        builder.Entity<Credenciales.CredencialesDelCliente>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "CredencialesDeClientes", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.EfirmaRfc).HasMaxLength(Credenciales.CredencialesConsts.RfcLength);
            b.Property(x => x.CiecRfc).HasMaxLength(Credenciales.CredencialesConsts.RfcLength);
            b.Property(x => x.NumeroDeCertificado).HasMaxLength(Credenciales.CredencialesConsts.NumeroDeCertificadoLength);
            b.HasIndex(x => x.OrganizacionId).IsUnique();
        });

        // El NIP de cada persona: solo su huella PBKDF2 y los intentos fallidos.
        builder.Entity<Credenciales.NipDeUsuario>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "NipsDeUsuarios", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => x.UsuarioId).IsUnique();
        });

        // Bitacora de credenciales: quien vio, descargo, cambio o borro que y cuando (nunca el dato).
        builder.Entity<Credenciales.UsoDeCredencial>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "UsosDeCredenciales", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.OrganizacionId, x.CreationTime });
        });

        // Perfil fiscal — AppPerfilesFiscales (uno por organizacion) y AppDatosDePerfiles (un
        // dato por campo del catalogo, con su origen).
        builder.Entity<PerfilFiscal>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "PerfilesFiscales", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.AvisosDeLaCsf).HasMaxLength(PerfilFiscal.MaxAvisosLength);
            // UNO por cliente: dos perfiles harian ambiguo cual es «el» del cliente.
            b.HasIndex(x => x.OrganizacionId).IsUnique();
            b.HasMany(x => x.Datos).WithOne().HasForeignKey(x => x.PerfilFiscalId).IsRequired();
            b.Navigation(x => x.Datos).AutoInclude();
        });

        builder.Entity<DatoDelPerfil>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "DatosDePerfiles", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasKey(x => new { x.PerfilFiscalId, x.Campo });
            b.Property(x => x.Campo).HasMaxLength(64);
            b.Property(x => x.Valor).IsRequired().HasMaxLength(CamposDelPerfil.MaxValorLength);
        });

        // Aceptaciones del aviso de privacidad — tabla AppConsentimientosDePrivacidad. Solo se
        // anaden filas (es la prueba de que cada quien acepto que texto y cuando).
        builder.Entity<Privacidad.ConsentimientoDePrivacidad>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "ConsentimientosDePrivacidad", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Version).IsRequired().HasMaxLength(Privacidad.AvisoDePrivacidadConsts.MaxVersionLength);
            // Una fila por persona y version: aceptar dos veces la misma no duplica (y la
            // carrera de dos pestanas a la vez la para la base).
            b.HasIndex(x => new { x.UserId, x.Version }).IsUnique();
        });

        // Chat — AppConversaciones (una abierta por persona y cliente) y AppMensajesDeConversaciones
        // (solo se anaden). Bloques es el turno tal cual se le repite al modelo, en JSON. Texto y no
        // jsonb a proposito: jsonb reordena las claves y el discriminador de tipo tiene que ir primero.
        builder.Entity<Conversacion>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "Conversaciones", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.UsuarioId, x.OrganizacionId, x.Cerrada });
        });

        builder.Entity<MensajeDeConversacion>(b =>
        {
            b.ToTable(AuxiliarContableConsts.DbTablePrefix + "MensajesDeConversaciones", AuxiliarContableConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Texto).IsRequired().HasMaxLength(ConversacionConsts.MaxTextoLength);
            b.Property(x => x.Bloques).IsRequired();
            b.HasIndex(x => new { x.ConversacionId, x.Numero }).IsUnique();
            b.HasIndex(x => x.CreadoUtc);
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
