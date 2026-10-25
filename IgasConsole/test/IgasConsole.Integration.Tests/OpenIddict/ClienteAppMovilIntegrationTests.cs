using System.Linq;
using System.Threading.Tasks;
using IgasConsole.OpenIddict;
using OpenIddict.Abstractions;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.OpenIddict.Applications;
using Xunit;

namespace IgasConsole;

/// <summary>
/// El cliente de la app móvil (Roque98/ZIA-App): nativo, público, solo vuelve a zia://auth y con
/// PKCE obligatorio. El migrador siembra en CADA despliegue y el UpdateAsync de ABP reescribe las
/// «requirements», así que se siembra dos veces para ver que PKCE sobrevive.
/// </summary>
public class ClienteAppMovilIntegrationTests : IntegrationTestBase
{
    private readonly IAbpApplicationManager _aplicaciones;

    public ClienteAppMovilIntegrationTests(PostgresFixture fixture) : base(fixture)
    {
        _aplicaciones = GetRequiredService<IAbpApplicationManager>();
    }

    private Task SembrarAsync() => WithUnitOfWorkAsync(() =>
        GetRequiredService<OpenIddictDataSeedContributor>().SeedAsync(new DataSeedContext()));

    [Fact]
    public async Task La_app_movil_es_un_cliente_nativo_con_pkce_obligatorio_aunque_se_siembre_otra_vez()
    {
        await SembrarAsync();
        await SembrarAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            var app = await _aplicaciones.FindByClientIdAsync(OpenIddictDataSeedContributor.MobileClientId);
            app.ShouldNotBeNull();
            (await _aplicaciones.GetApplicationTypeAsync(app)).ShouldBe(OpenIddictConstants.ApplicationTypes.Native);
            (await _aplicaciones.GetClientTypeAsync(app)).ShouldBe(OpenIddictConstants.ClientTypes.Public);
            (await _aplicaciones.GetRedirectUrisAsync(app)).ShouldBe(new[] { OpenIddictDataSeedContributor.MobileRedirectUri });
            (await _aplicaciones.HasRequirementAsync(app, OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange)).ShouldBeTrue();

            var permisos = await _aplicaciones.GetPermissionsAsync(app);
            permisos.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
            permisos.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
            // Un cliente público con «password» deja probar contraseñas contra /connect/token sin
            // pasar por la página de login (ver el cliente del Angular).
            permisos.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.Password);
            permisos.Count(p => p.StartsWith(OpenIddictConstants.Permissions.Prefixes.GrantType)).ShouldBe(2);
        });
    }
}
