using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Volo.Abp.Account.Web;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;
using Volo.Abp.Users;

namespace AuxiliarContable.Pages.Account;

/// <summary>
/// Añade el segundo factor al inicio de sesión.
///
/// <para>ABP open source YA detecta que un usuario necesita segundo factor —
/// <c>SignInManager</c> devuelve <c>RequiresTwoFactor</c>— pero deja el flujo sin
/// implementar a propósito: <c>LoginModel.TwoFactorLoginResultAsync()</c> lanza
/// <c>NotImplementedException</c> con el comentario «Override this method to add 2FA for
/// your application». Esto es ese override.</para>
///
/// <para>Solo se sustituye el MODELO, no la vista: la pantalla de login sigue siendo la de
/// ABP y se actualizará con el módulo. Por eso la clase lleva <c>ReplaceServices</c> en vez
/// de venir acompañada de un <c>Login.cshtml</c> copiado.</para>
///
/// <para>⚠️ La clase base es <c>OpenIddictSupportedLoginModel</c> y NO <c>LoginModel</c>,
/// porque el host usa <c>AbpAccountWebOpenIddictModule</c>. Heredar de <c>LoginModel</c>
/// compilaría igual, pero se perdería el manejo del <c>ReturnUrl</c> de OpenIddict y el
/// login dejaría de completar el flujo de autorización.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(
    typeof(AuxiliarContableLoginModel),
    typeof(OpenIddictSupportedLoginModel),
    typeof(LoginModel))]
public class AuxiliarContableLoginModel : OpenIddictSupportedLoginModel
{
    private readonly AuxiliarContable.HealthChecks.RegistradorDeSesiones _sesiones;

    public AuxiliarContableLoginModel(
        AuxiliarContable.HealthChecks.RegistradorDeSesiones sesiones,
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<AbpAccountOptions> accountOptions,
        IOptions<IdentityOptions> identityOptions,
        IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
        AbpOpenIddictRequestHelper openIddictRequestHelper,
        IWebHostEnvironment webHostEnvironment)
        : base(
            schemeProvider,
            accountOptions,
            identityOptions,
            identityDynamicClaimsPrincipalContributorCache,
            openIddictRequestHelper,
            webHostEnvironment)
    {
        _sesiones = sesiones;
    }

    /// <summary>
    /// Tras un login correcto, deja constancia de la sesión y refirma la cookie con su
    /// identificador.
    /// <para>Se hace aquí y no en la fábrica de claims porque lo que interesa registrar es
    /// el ACTO de entrar, no cada vez que se construye un principal — que ocurre también al
    /// refrescar. Si no, cada refresco abriría una sesión nueva y la lista sería inútil.</para>
    /// </summary>
    public override async Task<IActionResult> OnPostAsync(string action)
    {
        var resultado = await base.OnPostAsync(action);

        // Solo si de verdad quedó sesión iniciada: aquí se vuelve también cuando la
        // contraseña falla, cuando hace falta segundo factor (lo registra esa página) y
        // cuando se cancela. Y solo si no hay ya un session_id, para no duplicar.
        if (CurrentUser.IsAuthenticated && string.IsNullOrWhiteSpace(CurrentUser.FindSessionId()))
        {
            var usuario = await UserManager.GetByIdAsync(CurrentUser.GetId());
            await _sesiones.RegistrarAsync(SignInManager, usuario, LoginInput.RememberMe);
        }

        return resultado;
    }

    /// <summary>
    /// La contraseña era correcta pero falta el segundo factor.
    /// <para>En este punto el usuario NO tiene sesión todavía: Identity ha guardado una
    /// cookie intermedia que la página de segundo factor recupera con
    /// <c>GetTwoFactorAuthenticationUserAsync</c>. Por eso no hace falta arrastrar el id de
    /// usuario por la URL — y no debe hacerse: sería decirle a cualquiera qué cuenta está a
    /// medio autenticar.</para>
    /// </summary>
    protected override Task<IActionResult> TwoFactorLoginResultAsync()
    {
        return Task.FromResult<IActionResult>(RedirectToPage("./TwoFactor", new
        {
            returnUrl = ReturnUrl,
            returnUrlHash = ReturnUrlHash,
            rememberMe = LoginInput.RememberMe
        }));
    }
}
