using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Emailing;
using AuxiliarContable.Documentos;
using AuxiliarContable.IA.Claude;
using AuxiliarContable.MotorFiscal;
using AuxiliarContable.Notifications.Brevo;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using AuxiliarContable.Acceso;
using AuxiliarContable.BackgroundJobs;
using AuxiliarContable.ApiKeys;
using AuxiliarContable.Backups;
using AuxiliarContable.Jobs;
using AuxiliarContable.Monitoring;
using AuxiliarContable.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;
using OpenIddict.Server.AspNetCore;
using AuxiliarContable.DbExplorer;
using AuxiliarContable.EntityFrameworkCore;
using AuxiliarContable.HealthChecks;
using AuxiliarContable.Telegram;
using Microsoft.AspNetCore.Identity;
using AuxiliarContable.Observabilidad;
using AuxiliarContable.RateLimiting;
using Microsoft.OpenApi;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Studio;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Auditing;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Ui.LayoutHooks;
using Volo.Abp.VirtualFileSystem;
using AuxiliarContable.HttpApi.Host.Branding;
using Volo.Abp.Uow;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Microsoft.AspNetCore.Hosting;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;
using Volo.Abp.Swashbuckle;
using Volo.Abp.Studio.Client.AspNetCore;
using Volo.Abp.Security.Claims;

namespace AuxiliarContable;

[DependsOn(
    typeof(AuxiliarContableHttpApiModule),
    typeof(AbpStudioClientAspNetCoreModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(AuxiliarContableApplicationModule),
    typeof(AuxiliarContableEntityFrameworkCoreModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpAspNetCoreSerilogModule),
    // Explicito aunque ya venga por AbpBackgroundJobs: el recolector de metricas
    // depende de el y no queremos que se rompa si un dia cambia esa cadena.
    typeof(AbpBackgroundWorkersModule),
    // Documentos de los clientes en disco (cifrados por AlmacenDeDocumentos).
    typeof(Volo.Abp.BlobStoring.FileSystem.AbpBlobStoringFileSystemModule)
    )]
public class AuxiliarContableHttpApiHostModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("AuxiliarContable");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        if (!hostingEnvironment.IsDevelopment())
        {
            PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
            {
                options.AddDevelopmentEncryptionAndSigningCertificate = false;
            });

            PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
            {
                serverBuilder.AddProductionEncryptionAndSigningCertificate("openiddict.pfx", configuration["AuthServer:CertificatePassPhrase"]!);
                serverBuilder.SetIssuer(new Uri(configuration["AuthServer:Authority"]!));
            });
        }
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        if (!configuration.GetValue<bool>("App:DisablePII"))
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.LogCompleteSecurityArtifact = true;
        }

        if (!configuration.GetValue<bool>("AuthServer:RequireHttpsMetadata"))
        {
            Configure<OpenIddictServerAspNetCoreOptions>(options =>
            {
                options.DisableTransportSecurityRequirement = true;
            });
        }

        // FUERA del if de arriba a proposito. La plantilla de ABP solo configuraba esto
        // cuando NO se exige HTTPS, pero detras de un proxy inverso que termina TLS
        // (nginx -> Kestrel en 127.0.0.1) hace mas falta que nunca: sin ello la app se
        // cree en http, OpenIddict rechaza el discovery con "This server only accepts
        // HTTPS requests" y el login no arranca.
        Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
            // Por defecto solo se confia en el loopback IPv6 (::1); nginx conecta desde
            // 127.0.0.1 y la cabecera se descartaba en silencio.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        });

        if (hostingEnvironment.IsDevelopment())
        {
            context.Services.AddRazorPages()
                .AddRazorRuntimeCompilation();
        }

        ConfigureStudio(hostingEnvironment);
        ConfigureAuthentication(context);
        ConfigureUrls(configuration);
        ConfigureBundles(hostingEnvironment);
        ConfigureConventionalControllers();
        ConfigureHealthChecks(context);
        ConfigureSwagger(context, configuration);
        ConfigureVirtualFileSystem(context);
        ConfigureCors(context, configuration);
        ConfigureDataProtection(context, configuration, hostingEnvironment);

        // Explorador de BD: SOLO habilitado en Development.
        Configure<DbExplorerOptions>(options =>
        {
            options.IsEnabled = hostingEnvironment.IsDevelopment();
        });

        // Color de acento del inquilino en las paginas MVC (la de login, en la practica).
        //
        // Head.Last y no Body.*: es el ultimo punto del <head>, asi que la regla gana a las
        // del tema sin necesidad de !important y sin que la pagina llegue a pintarse una vez
        // con el color viejo. El tema LeptonX Lite renderiza ese hook (comprobado en su
        // ensamblado, que invoca "Header.Last").
        //
        // El nombre y el logo NO van por aqui: para esos el tema ya pregunta a
        // IBrandingProvider, que es AuxiliarContableBrandingProvider.
        Configure<AbpLayoutHookOptions>(options =>
        {
            options.Add(LayoutHooks.Head.Last, typeof(BrandingStyleViewComponent));
        });

        // Trabajos en segundo plano: conservar los que TERMINAN BIEN.
        //
        // Por defecto ABP los BORRA al completarse, y entonces el panel de /jobs solo
        // podria ensenar lo que esta roto. Una cola donde solo se ve lo que falla no deja
        // distinguir "no hay problemas" de "el worker no esta corriendo", que es
        // justamente la pregunta que se hace uno al abrirla.
        //
        // La retencion es corta (JobConsts.SuccessfulJobRetentionDays) porque un
        // completado solo sirve para ver latido; lo que interesa guardar es lo que fallo, y
        // eso vive en AppJobFailures con su propia retencion, mas larga.
        //
        // ABP registra su BackgroundJobCleanupWorker solo si estas dos opciones estan
        // puestas (verificado en refs/abp, AbpBackgroundJobsModule): sin ellas no habria
        // quien borrase los completados.
        Configure<AbpBackgroundJobWorkerOptions>(options =>
        {
            options.StoreSuccessfulJobs = true;
            options.SuccessfulJobRetentionTime =
                TimeSpan.FromDays(JobConsts.SuccessfulJobRetentionDays);
        });

        // Rutas de los archivos de estado que dejan los scripts de copia. En desarrollo no
        // existen y el lector devuelve null, que es lo correcto: aqui no hay copias.
        Configure<BackupOptions>(configuration.GetSection("Backups"));

        // Monitoreo: umbrales y credenciales de Telegram desde la seccion "Monitoring"
        // (en produccion llegan por variables de entorno desde /etc/auxiliar-contable/env).
        Configure<MonitoringOptions>(configuration.GetSection("Monitoring"));
        // Lo necesita TelegramAlertNotifier. IHttpClientFactory y no un HttpClient suelto:
        // un HttpClient por aviso agota los sockets del sistema.
        context.Services.AddHttpClient();

        // Brevo: correo (sustituye al NullEmailSender del dominio), SMS y WhatsApp. La API
        // key llega por user-secrets en dev y por /etc/auxiliar-contable/env en produccion.
        Configure<BrevoOptions>(configuration.GetSection(BrevoOptions.Seccion));
        context.Services.AddHttpClient(BrevoClient.NombreHttpClient, cliente =>
        {
            cliente.Timeout = TimeSpan.FromSeconds(20);
        });
        context.Services.Replace(ServiceDescriptor.Transient<IEmailSender, BrevoEmailSender>());

        // Claude (IA): el chat del asistente. La API key
        // llega igual que la de Brevo; sin ella el modelo queda «sin configurar» y no se llama.
        Configure<ClaudeOptions>(configuration.GetSection(ClaudeOptions.Seccion));
        var claude = configuration.GetSection(ClaudeOptions.Seccion).Get<ClaudeOptions>() ?? new ClaudeOptions();
        context.Services.AddHttpClient(ClaudeModeloDeIA.NombreHttpClient, cliente =>
        {
            cliente.Timeout = TimeSpan.FromSeconds(claude.TimeoutSegundos);
        });

        // Verificación de CFDI del SAT (servicio público del código QR): el estatus de cada factura cada noche.
        context.Services.AddHttpClient(AuxiliarContable.HealthChecks.VerificadorDeCfdiSat.NombreHttpClient, cliente =>
        {
            cliente.Timeout = TimeSpan.FromSeconds(20);
        });

        // Descarga Masiva del SAT (servicio oficial con la e.firma del cliente): un paquete puede tardar en bajar.
        context.Services.AddHttpClient(AuxiliarContable.HealthChecks.ServicioDescargaMasivaSat.NombreHttpClient, cliente =>
        {
            cliente.Timeout = TimeSpan.FromMinutes(5);
            cliente.MaxResponseContentBufferSize = 300L * 1024 * 1024;
        });

        // Motor fiscal (motores-fiscales): servicio Python en 127.0.0.1 del mismo servidor. Sin
        // MotorFiscal:Url queda «sin configurar» y el chat dice que no pudo consultar.
        Configure<MotorFiscalOptions>(configuration.GetSection(MotorFiscalOptions.Seccion));
        var motor = configuration.GetSection(MotorFiscalOptions.Seccion).Get<MotorFiscalOptions>() ?? new MotorFiscalOptions();
        context.Services.AddHttpClient(MotorFiscalHttp.NombreHttpClient, cliente =>
        {
            // El máximo de todos: cada operación pone su propio tope (MotorFiscalHttp).
            cliente.Timeout = TimeSpan.FromSeconds(Math.Max(motor.TimeoutSegundos, motor.TimeoutDeBancosSegundos) + 5);
        });

        // Documentos de los clientes: AL DISCO, no a la base (decision del dueño, 2026-10-25),
        // y siempre cifrados (AlmacenDeDocumentos). Solo este contenedor: las fotos y logos
        // siguen en la base. En produccion la carpeta va en /var/lib/auxiliar-contable, la unica
        // escribible con ProtectSystem=strict, y entra en la copia remota (backup-remoto.sh).
        Configure<DocumentosOptions>(configuration.GetSection(DocumentosOptions.Seccion));
        Configure<AuxiliarContable.Credenciales.CredencialesOptions>(configuration.GetSection(AuxiliarContable.Credenciales.CredencialesOptions.Seccion));
        var carpetaDeDocumentos = configuration[$"{DocumentosOptions.Seccion}:Carpeta"];
        if (string.IsNullOrWhiteSpace(carpetaDeDocumentos))
        {
            carpetaDeDocumentos = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "auxiliar-contable", "documentos");
        }

        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.Configure<DocumentosContainer>(container =>
            {
                container.UseFileSystem(disco => disco.BasePath = carpetaDeDocumentos);
            });
        });

        // Antivirus de lo que se sube: ClamAV del propio VPS si hay Antivirus:Host. Sin él queda
        // SinEscaner (Domain) y cada subida lo apunta en el log.
        Configure<AuxiliarContable.Antivirus.AntivirusOptions>(configuration.GetSection(AuxiliarContable.Antivirus.AntivirusOptions.Seccion));
        if (!string.IsNullOrWhiteSpace(configuration[$"{AuxiliarContable.Antivirus.AntivirusOptions.Seccion}:Host"]))
        {
            context.Services.Replace(ServiceDescriptor.Singleton<IEscanerDeArchivos, AuxiliarContable.Antivirus.ClamAvEscaner>());
        }

        // Rate limiting por IP. Igual que Monitoring: umbrales desde appsettings, no
        // desde AbpSettings, porque se leen en caliente en cada peticion.
        // OpenTelemetry → OpenObserve (solo si hay Observabilidad:Otlp:Endpoint; docs/observabilidad.md).
        context.Services.AddObservabilidad(configuration, hostingEnvironment);

        Configure<RateLimitingOptions>(configuration.GetSection("RateLimiting"));

        // Bloqueo automatico de IPs. Apagado por defecto: encenderlo hace que la aplicacion
        // empiece a dejar gente fuera sola, y eso hay que quererlo a proposito.
        Configure<AutoBlockOptions>(configuration.GetSection("Security:AutoBlock"));
        context.Services.AddAuxiliarContableRateLimiting();

        // Ocultar la puerta del superadministrador: mueve el panel fuera de la raiz y el
        // formulario de login fuera de /Account/Login. Vacio = comportamiento por defecto,
        // que es lo que corre en Development y el interruptor de emergencia en produccion.
        //
        // Va DESPUES de AddAuxiliarContableRateLimiting a proposito: aquel lee el segmento por
        // IOptions<AccesoOptions> en cada peticion para saber que ruta lleva el limite
        // estricto de intentos de contraseña, asi que el orden de registro no importa, pero
        // leerlos juntos si ayuda a no olvidarse de que estan atados.
        context.Services.AddAuxiliarContableAcceso(configuration);

        // Auditoria: ABP NO audita los GET por defecto, asi que el registro solo tenia
        // POST/PUT y no servia para ver que estaba pidiendo alguien. Con esto entra
        // tambien la lectura, que es justo lo que hacen los bots al sondear rutas.
        //
        // El coste es una fila por peticion: acotado porque el rate limiter corta a
        // 120/min por IP ANTES de llegar a MVC (un 429 no genera fila) y porque el
        // recolector purga por retencion una vez al dia. Los endpoints que sondea la
        // propia pagina llevan [DisableAuditing] para no auditarse a si misma.
        Configure<AbpAuditingOptions>(options =>
        {
            options.IsEnabledForGetRequests = true;

            // Historial de cambios de entidades: quien cambio QUE CAMPO y de que valor a
            // cual. Las tablas (AbpEntityChanges / AbpEntityPropertyChanges) estaban desde
            // la migracion inicial pero sin nadie que las llenara.
            //
            // ⚠️ LA LISTA ES EXPLICITA, NUNCA AddAllEntities(). Seleccionar todo seria
            // catastrofico en ESTA aplicacion: ServerMetric inserta una fila POR MINUTO,
            // y RateLimitEvent, Notificacion y JobFailure escriben continuamente. Cada
            // insercion generaria una fila de cambio mas una por columna — las tablas de
            // auditoria creceria mas rapido que los datos que auditan.
            //
            // Se rastrea lo que cuesta reconstruir a mano y donde importa QUIEN lo hizo:
            // quién mete a quién en qué organización (decide quién ve qué de cada cliente), y las dos cosas con consecuencias de seguridad (quien vetó una
            // IP, quien creó o revocó una clave). Mas los usuarios y roles de Identity,
            // que es donde vive "quien le dio permisos de administrador a quien".
            options.EntityHistorySelectors.Add(new NamedTypeSelector(
                "AuxiliarContable.Seguimiento",
                tipo =>
                    tipo == typeof(AuxiliarContable.Organizaciones.Organizacion) ||
                    tipo == typeof(AuxiliarContable.Organizaciones.OrganizacionMiembro) ||
                    tipo == typeof(BlockedIp) ||
                    tipo == typeof(ApiKey) ||
                    tipo == typeof(Volo.Abp.Identity.IdentityUser) ||
                    tipo == typeof(Volo.Abp.Identity.IdentityRole)));
        });

        // Sondeos internos FUERA del registro. Sin esto el colector de health checks
        // (cada 10 s) mete ~8.600 filas de ruido al dia y entierra el trafico real.
        // Va aparte del [DisableAuditing] de MetricsAppService porque estos NO son
        // application services: /health-status lo mapea MapHealthChecks, asi que el
        // atributo no lo alcanza y hay que excluirlo por URL.
        Configure<AbpAspNetCoreAuditingOptions>(options =>
        {
            options.IgnoredUrls.Add("/health-status");
            options.IgnoredUrls.Add("/health-ui");
            options.IgnoredUrls.Add("/health-api");
            // Los nodos llaman varias veces por minuto cada uno, y la activación lleva la
            // clave de la licencia: nada de eso va al registro de peticiones.
            options.IgnoredUrls.Add("/api/nodo");
        });

        // Segundo factor por Telegram. Se añade al mapa de proveedores de Identity en vez de
        // tocar el IdentityBuilder, que ya lo monta AbpIdentityAspNetCoreModule.
        //
        // El NOMBRE tiene que ser el mismo aqui, al generar el codigo y al validarlo: si no
        // coincide, nada falla al compilar — el codigo simplemente nunca valida. Por eso vive
        // en una constante.
        //
        // TokenProviderDescriptor recibe el TIPO, e Identity lo resuelve del contenedor; el
        // proveedor se registra solo por implementar ITransientDependency.
        Configure<IdentityOptions>(options =>
        {
            options.Tokens.ProviderMap[TelegramTwoFactorConsts.ProviderName] =
                new TokenProviderDescriptor(typeof(TelegramTwoFactorTokenProvider));
        });

        // Los códigos por correo y SMS son los de ABP: de un solo uso, cifrados en
        // AbpUserTokens y con caducidad absoluta (3 minutos de serie). Se alarga a lo que
        // prometen los mensajes: un correo puede tardar en llegar más de 3 minutos.
        Configure<Volo.Abp.Identity.AspNetCore.AbpEmailTwoFactorTokenProviderOptions>(o =>
            o.TokenLifespan = System.TimeSpan.FromMinutes(AuxiliarContable.Seguridad.SeguridadConsts.MinutosDeValidez));
        Configure<Volo.Abp.Identity.AspNetCore.AbpPhoneNumberTwoFactorTokenProviderOptions>(o =>
            o.TokenLifespan = System.TimeSpan.FromMinutes(AuxiliarContable.Seguridad.SeguridadConsts.MinutosDeValidez));
    }

    private void ConfigureDataProtection(
        ServiceConfigurationContext context,
        IConfiguration configuration,
        IHostEnvironment hostingEnvironment)
    {
        // Persiste las claves de DataProtection para que SOBREVIVAN a los reinicios.
        // Sin esto ABP usa claves efímeras: cada arranque las regenera e invalida las
        // cookies/antiforgery emitidas antes -> 400 "different claims-based user" al loguear.
        //
        // En el VPS hay que apuntarlo FUERA del directorio de publicación (p. ej.
        // /var/lib/auxiliar-contable/keys) con App:DataProtectionKeysPath o la variable de
        // entorno App__DataProtectionKeysPath: cada deploy reemplaza ese directorio y se
        // llevaría las claves por delante. El fallback relativo es el layout de desarrollo.
        var configuredPath = configuration["App:DataProtectionKeysPath"];
        var keysFolder = configuredPath.IsNullOrWhiteSpace()
            ? Path.Combine(hostingEnvironment.ContentRootPath, "..", "..", "data-protection-keys")
            : configuredPath!;
        Directory.CreateDirectory(keysFolder);

        context.Services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
            .SetApplicationName("AuxiliarContable");
    }

    private void ConfigureStudio(IHostEnvironment hostingEnvironment)
    {
        if (hostingEnvironment.IsProduction())
        {
            Configure<AbpStudioClientOptions>(options =>
            {
                options.IsLinkEnabled = false;
            });
        }
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;

            // Arrastra el session_id de la cookie a TODO principal que se reconstruya.
            // Sin esto el access token sale sin el y cerrar una sesion no corta al front:
            // el endpoint de autorizacion no reutiliza el principal de la cookie, lo crea
            // de nuevo desde el usuario. Ver SessionIdClaimContributor.
            options.Contributors.Add(typeof(SessionIdClaimContributor));
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.Applications["Angular"].RootUrl = configuration["App:AngularUrl"];
            options.Applications["Angular"].Urls[AccountUrlNames.PasswordReset] = "account/reset-password";
            options.RedirectAllowedUrls.AddRange(configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());
        });
    }

    private void ConfigureBundles(IHostEnvironment hostingEnvironment)
    {
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-styles.css");
                }
            );

            options.ScriptBundles.Configure(
                LeptonXLiteThemeBundles.Scripts.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-scripts.js");
                    if (hostingEnvironment.IsDevelopment())
                    {
                        bundle.AddFiles("/dev-login-helper.js");
                    }
                }
            );
        });
    }


    private void ConfigureVirtualFileSystem(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<AuxiliarContableDomainSharedModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AuxiliarContable.Domain.Shared"));
                options.FileSets.ReplaceEmbeddedByPhysical<AuxiliarContableDomainModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AuxiliarContable.Domain"));
                options.FileSets.ReplaceEmbeddedByPhysical<AuxiliarContableApplicationContractsModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AuxiliarContable.Application.Contracts"));
                options.FileSets.ReplaceEmbeddedByPhysical<AuxiliarContableApplicationModule>(Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}AuxiliarContable.Application"));
            });
        }
    }

    private void ConfigureConventionalControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AuxiliarContableApplicationModule).Assembly);
        });
    }

    private static void ConfigureSwagger(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAbpSwaggerGenWithOidc(
            configuration["AuthServer:Authority"]!,
            ["AuxiliarContable"],
            [AbpSwaggerOidcFlows.AuthorizationCode],
            null,
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "AuxiliarContable API", Version = "v1" });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
            });
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .WithOrigins(
                        configuration["App:CorsOrigins"]?
                            .Split(",", StringSplitOptions.RemoveEmptyEntries)
                            .Select(o => o.Trim().RemovePostFix("/"))
                            .ToArray() ?? Array.Empty<string>()
                    )
                    .WithAbpExposedHeaders()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    /// <summary>
    /// El recolector se registra SOLO aqui, en el host. El DbMigrator carga el modulo de
    /// dominio pero no este, asi que no arranca un worker en un proceso que solo migra.
    /// </summary>
    public override async Task OnPostApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var opciones = context.ServiceProvider
            .GetRequiredService<IOptions<MonitoringOptions>>().Value;

        if (opciones.IsEnabled)
        {
            await context.AddBackgroundWorkerAsync<MetricsCollectorWorker>();
        }

        // Escucha los /start del bot para vincular cuentas. Va con la misma condicion que
        // el recolector: comparten el interruptor de monitoreo y el bot de Telegram, y
        // asi el DbMigrator y los tests tampoco lo arrancan.
        //
        // OJO: solo UN proceso puede sondear getUpdates de un bot a la vez. Si algun dia
        // se levanta una segunda instancia con el mismo token, se robaran los mensajes
        // entre ellas y las vinculaciones fallaran de forma intermitente.
        if (opciones.IsEnabled)
        {
            await context.AddBackgroundWorkerAsync<TelegramLinkWorker>();
        }

        // SIN condicion, al reves que los dos de arriba: el bloqueo por IP es seguridad y
        // no monitoreo. Si dependiera del interruptor de monitoreo, apagarlo dejaria de
        // aplicar los bloqueos en silencio — y nadie relaciona una cosa con la otra.
        await context.AddBackgroundWorkerAsync<BlockedIpsRefreshWorker>();

        // Tambien sin condicion: el propio worker mira su interruptor y no hace nada si
        // esta apagado. Registrarlo siempre evita que encenderlo requiera un redespliegue
        // distinto al de cambiar el umbral.
        await context.AddBackgroundWorkerAsync<AutoBlockWorker>();

        // Purga de sesiones sin actividad. Tampoco depende del interruptor de monitoreo:
        // si no corriera, la lista de sesiones se llenaria de filas muertas.
        await context.AddBackgroundWorkerAsync<SessionsPurgeWorker>();

        // Purga del historial de fallos de trabajos. Nada mas lo borra: el historial
        // sobrevive a proposito a la fila del trabajo que lo genero.
        await context.AddBackgroundWorkerAsync<JobFailuresPurgeWorker>();

        // Vigila que no haya trabajos abandonados. Sin condicion: que la aplicacion haya
        // dejado de intentar algo hay que saberlo aunque el monitoreo este apagado.
        await context.AddBackgroundWorkerAsync<AbandonedJobsWatchWorker>();

        // Purga de avisos caducados de la campana.
        await context.AddBackgroundWorkerAsync<NotificationsPurgeWorker>();

        // Vigila las copias de seguridad. Sin esto, que la copia lleve semanas rota solo
        // se descubriria el dia de intentar restaurar — el peor dia posible.
        //
        // NO en Development: ahi no hay copias por diseno, asi que el vigilante avisaria
        // de su ausencia en cada arranque. Un aviso que siempre esta puesto y que nadie
        // puede resolver ensena a ignorar la campana, y entonces el aviso del dia que
        // importe tampoco se leera.
        if (!context.ServiceProvider.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            await context.AddBackgroundWorkerAsync<BackupWatchWorker>();
        }

        // Avisa a cada dueno de que su clave de API va a caducar. Hoy caducan en silencio
        // y el script que las usaba empieza a recibir 401 sin explicacion.
        await context.AddBackgroundWorkerAsync<ApiKeyExpiryWatchWorker>();

        // Los impuestos del mes al día: recalcula lo que quedó «por actualizar» al entrar una factura.
        await context.AddBackgroundWorkerAsync<ActualizadorDeImpuestosWorker>();

        // Cada noche, el estatus de las facturas en el SAT (servicio público de verificación).
        await context.AddBackgroundWorkerAsync<RevisorDeEstatusSatWorker>();
        await context.AddBackgroundWorkerAsync<DescargaMasivaWorker>();
        await context.AddBackgroundWorkerAsync<ContabilidadAlDiaWorker>();
        await context.AddBackgroundWorkerAsync<InterpretacionesWorker>();
        await context.AddBackgroundWorkerAsync<Normativa.AnalisisNormativoWorker>();
        await context.AddBackgroundWorkerAsync<Contabilidad.ConciliacionIntegralWorker>();

        await FecharFacturasSinFechaAsync(context.ServiceProvider);
    }

    /// <summary>
    /// Las facturas que se subieron antes de que la bóveda leyera la fecha de su XML quedaron en el mes
    /// en que se subieron, y la conciliación de su mes no las encontraba. Se fechan una vez; cuando ya no
    /// queda ninguna, esto es una consulta vacía. Si falla, la aplicación arranca igual.
    /// </summary>
    private static async Task FecharFacturasSinFechaAsync(IServiceProvider servicios)
    {
        var logger = servicios.GetRequiredService<ILogger<AuxiliarContableHttpApiHostModule>>();
        try
        {
            using var uow = servicios.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var gestor = servicios.GetRequiredService<GestorDeDocumentos>();
            var fechadas = await gestor.FecharFacturasSinFechaAsync();
            var identificadas = await gestor.IdentificarFacturasAsync();
            await uow.CompleteAsync();
            if (fechadas > 0)
            {
                logger.LogInformation("Se fecharon {Cantidad} facturas con la fecha de su XML.", fechadas);
            }

            if (identificadas > 0)
            {
                logger.LogInformation("Se leyó el UUID de {Cantidad} facturas.", identificadas);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudieron fechar las facturas viejas; se intentará en el siguiente arranque.");
        }
    }

    private void ConfigureHealthChecks(ServiceConfigurationContext context)
    {
        context.Services.AddAuxiliarContableHealthChecks();
    }


    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        app.UseForwardedHeaders();

        // Justo despues de las cabeceras reenviadas: asi el cronometro abarca todo el
        // resto del pipeline (autenticacion, rutas, el endpoint y la serializacion).
        app.UseMiddleware<RequestMetricsMiddleware>();

        // Bloqueo por IP, LO ANTES POSIBLE. Va aqui por dos motivos:
        //  - DESPUES de UseForwardedHeaders, o detras de nginx toda peticion pareceria venir
        //    de 127.0.0.1 y bloquear una IP no habria bloqueado a nadie.
        //  - DESPUES de RequestMetricsMiddleware, para que un bloqueado siga contando en las
        //    metricas: si no, el trafico que se corta desapareceria de los graficos y no se
        //    veria que el bloqueo esta haciendo algo.
        // Y antes de todo lo demas: a quien esta vetado no se le dedica ni rutas, ni CORS,
        // ni autenticacion, ni una conexion a la base.
        app.UseMiddleware<BlockedIpMiddleware>();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment())
        {
            app.UseErrorPage();

            // ⚠️ JUSTO DESPUES de UseErrorPage, nunca antes. UseErrorPage es
            // UseStatusCodePagesWithRedirects: envuelve lo que se registre DESPUES y convierte
            // cualquier 4xx/5xx SIN CUERPO en un 302 a la pagina de error. Este middleware le
            // pone cuerpo a esas respuestas mientras vuelven, con lo que StatusCodePages se
            // abstiene y el codigo de estado llega intacto.
            //
            // Los cuatro sitios que ya escribian cuerpo a mano (429 del rate limiting, 403 del
            // bloqueo de IPs, 401 de las claves de API, 404 del logo) siguen igual: este pasa
            // de largo si la respuesta ya trae ContentType. Lo que cubre es todo lo que NO
            // escribimos nosotros — rutas inexistentes, metodo no permitido— que en produccion
            // respondian con un redirect.
            //
            // Registrado por FUERA no haria nada, y no fallaria nada: solo se ve en produccion.
            app.UseMiddleware<CuerpoEnLosErroresMiddleware>();
        }

        app.UseRouting();

        app.MapAbpStaticAssets();
        app.UseAbpStudioLink();
        app.UseAbpSecurityHeaders();
        app.UseCors();

        // La POSICION de esta linea importa mucho, y estuvo mal. Va despues de:
        //
        //  - UseRouting, para que OnRejected tenga GetEndpoint() resuelto y pueda
        //    etiquetar el patron de ruta en vez de la URL con ids.
        //
        //  - MapAbpStaticAssets, porque los estaticos NO deben contar. La pagina de login
        //    de ABP se trae decenas de ficheros (/libs, /Themes, fuentes, scripts) del
        //    host de la API: contandolos, UNA sola carga de pagina agotaba el limite y a
        //    partir de ahi todo respondia 429.
        //
        //  - UseCors, para que un 429 lleve cabeceras CORS. Sin ellas el navegador no
        //    puede ni leer la respuesta: la reporta como fallo de CORS, y el
        //    descubrimiento de OpenID y la configuracion de la app fallan con un error
        //    que no se parece en nada a "te he limitado". La aplicacion entera se rompia.
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        // Claves de API. DESPUES de la autenticacion normal a proposito: sobrescribe
        // context.User, asi que una peticion con X-Api-Key manda sobre cualquier cookie o
        // token que traiga tambien. Y ANTES de UseAuthorization, que es quien decide.
        app.UseMiddleware<ApiKeyMiddleware>();

        // Sesiones. DESPUES de que la autenticacion haya construido el principal —de ahi
        // sale el claim— y ANTES de UseAuthorization, que es quien decide. Si la sesion ya
        // no existe, la peticion sigue como anonima y el resto del pipeline hace lo suyo.
        app.UseMiddleware<SessionValidationMiddleware>();

        // Un segundo envío del login con sesión ya iniciada (doble toque en «Entrar») va a su
        // ReturnUrl en vez de chocar con el antifalsificación (error 400). DESPUÉS de validar la
        // sesión: si ya no vale, la petición es anónima y el login sigue normal.
        app.UseMiddleware<AuxiliarContable.Acceso.LoginYaIniciadoMiddleware>();

        // Limite de peticiones POR IDENTIDAD (usuario y clave de API).
        //
        // ⚠️ AQUI Y NO EN UseRateLimiter(). Aquel va antes de UseAuthentication —y tiene
        // que seguir ahi, porque su trabajo es cortar avalanchas anonimas antes de que
        // lleguen a validar tokens o a buscar claves en la base—, y en ese punto
        // context.User esta vacio: no hay ninguna identidad que limitar. Este va despues de
        // ApiKeyMiddleware y de la validacion de sesion, cuando ya se sabe quien llama.
        //
        // Los dos conviven: la peticion pasa por ambos. El de IP protege de extranos; este,
        // de los propios — un script con un bucle roto sale de una IP legitima.
        app.UseMiddleware<IdentityRateLimitMiddleware>();

        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "AuxiliarContable API");

            var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
            options.OAuthClientId(configuration["AuthServer:SwaggerClientId"]);
        });
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}
