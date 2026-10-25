using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.Uow;

namespace IgasConsole.OpenIddict;

/* Creates initial data that is needed to property run the application
 * and make client-to-server communication possible.
 */
public class OpenIddictDataSeedContributor : OpenIddictDataSeedContributorBase, IDataSeedContributor, ITransientDependency
{
    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager)
        : base(configuration, openIddictApplicationRepository, applicationManager, openIddictScopeRepository, scopeManager)
    {
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await CreateScopesAsync();
        await CreateApplicationsAsync();
    }

    private async Task CreateScopesAsync()
    {
        await CreateScopesAsync(new OpenIddictScopeDescriptor 
        {
            Name = "IgasConsole", 
            DisplayName = "IgasConsole API", 
            Resources = { "IgasConsole" }
        });
    }

    private async Task CreateApplicationsAsync()
    {
        var commonScopes = new List<string> {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "IgasConsole"
        };

        var configurationSection = Configuration.GetSection("OpenIddict:Applications");


        // Console Test / Angular Client
        
        var appClientId = configurationSection["IgasConsole_App:ClientId"];
        if (!appClientId.IsNullOrWhiteSpace())
        {
            var appClientRootUrl = configurationSection["IgasConsole_App:RootUrl"]?.TrimEnd('/');
            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: appClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Console Test / Angular Application",
                secret: null,
                // Solo los dos flujos que el Angular usa de verdad. La plantilla de ABP
                // sembraba ademas Password, ClientCredentials, LinkLogin e Impersonation:
                // sobre un cliente PUBLICO, el flujo Password deja probar credenciales
                // directamente contra /connect/token con un script, sin pasar por la
                // pagina de login, y basta con 5 peticiones para bloquear al admin.
                // Para reactivar la suplantacion de usuarios, vuelve a anadir "Impersonation".
                grantTypes: new List<string> {
                    OpenIddictConstants.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.GrantTypes.RefreshToken
                },
                scopes: commonScopes,
                redirectUris: new List<string> { appClientRootUrl! },
                postLogoutRedirectUris: new List<string> { appClientRootUrl! },
                clientUri: appClientRootUrl,
                logoUri: "/images/clients/angular.svg"
            );
        }

        
        




        // Swagger Client
        var swaggerClientId = configurationSection["IgasConsole_Swagger:ClientId"];
        if (!swaggerClientId.IsNullOrWhiteSpace())
        {
            var swaggerRootUrl = configurationSection["IgasConsole_Swagger:RootUrl"]?.TrimEnd('/');

            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: swaggerClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Swagger Application",
                secret: null,
                grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode, },
                scopes: commonScopes,
                redirectUris: new List<string> { $"{swaggerRootUrl}/swagger/oauth2-redirect.html" },
                clientUri: swaggerRootUrl!.EnsureEndsWith('/') + "swagger",
                logoUri: "/images/clients/swagger.svg"
            );
        }



        // Panel del VPS en GitHub Pages: cliente público, código de autorización + PKCE.
        // Fijo en código (no por configuración) porque su URL es pública y no cambia por entorno.
        await CreateOrUpdateApplicationAsync(
            applicationType: OpenIddictConstants.ApplicationTypes.Web,
            name: "VpsDashboard",
            type: OpenIddictConstants.ClientTypes.Public,
            consentType: OpenIddictConstants.ConsentTypes.Implicit,
            displayName: "VPS Dashboard (GitHub Pages)",
            secret: null,
            grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode },
            scopes: commonScopes,
            redirectUris: new List<string> { "https://roque98.github.io/vps-dashboard/", "https://roque98.github.io/vps-dashboard/abp/" },
            postLogoutRedirectUris: new List<string> { "https://roque98.github.io/vps-dashboard/", "https://roque98.github.io/vps-dashboard/abp/" },
            clientUri: "https://roque98.github.io/vps-dashboard/"
        );

        // App móvil (Roque98/ZIA-App): cliente NATIVO público. El login abre el navegador del
        // sistema y vuelve a zia://auth. Fijo en código, como el del panel: el esquema es de la app.
        await CreateOrUpdateApplicationAsync(
            applicationType: OpenIddictConstants.ApplicationTypes.Native,
            name: MobileClientId,
            type: OpenIddictConstants.ClientTypes.Public,
            consentType: OpenIddictConstants.ConsentTypes.Implicit,
            displayName: "ZIA (app móvil)",
            secret: null,
            grantTypes: new List<string> {
                OpenIddictConstants.GrantTypes.AuthorizationCode,
                OpenIddictConstants.GrantTypes.RefreshToken
            },
            scopes: commonScopes,
            redirectUris: new List<string> { MobileRedirectUri },
            postLogoutRedirectUris: new List<string> { MobileRedirectUri }
        );
        await RequirePkceAsync(MobileClientId);
    }

    public const string MobileClientId = "IgasConsole_Mobile";
    public const string MobileRedirectUri = "zia://auth";

    /// <summary>
    /// PKCE obligatorio para este cliente. ⚠️ Un esquema propio (zia://) lo puede registrar otra
    /// app del teléfono: sin el verificador, el código que intercepte no le sirve. La base de ABP
    /// no recibe «requirements» y su UpdateAsync las reescribe, por eso se añade tras cada siembra.
    /// </summary>
    private async Task RequirePkceAsync(string clientId)
    {
        var client = await OpenIddictApplicationRepository.FindByClientIdAsync(clientId);
        var model = client!.ToModel();
        var descriptor = new AbpApplicationDescriptor();
        await ApplicationManager.PopulateAsync(descriptor, model);
        if (descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange))
        {
            await ApplicationManager.UpdateAsync(model, descriptor);
        }
    }
}
