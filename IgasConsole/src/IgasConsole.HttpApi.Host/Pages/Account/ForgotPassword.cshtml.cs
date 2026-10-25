using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Account.Emailing;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Identity;

namespace IgasConsole.Pages.Account;

/// <summary>
/// Sustituye la página de recuperación de ABP para aceptar TAMBIÉN el nombre de usuario,
/// no solo el correo.
/// <para>Motivo: la pantalla de login acepta usuario o correo, pero la de recuperación
/// solo correo, y eso confunde. El enlace sale por correo igualmente (ver
/// <c>CorreoAccountEmailer</c>), a la dirección que tenga la cuenta.</para>
///
/// <para>⚠️ Responde LO MISMO exista o no la cuenta. Con envío real, decir «no existe» la
/// convertiría en un comprobador de usuarios y correos.</para>
/// <para>NO hereda de <c>ForgotPasswordModel</c> a propósito: esa clase declara
/// <c>Email</c> con <c>[Required]</c> y <c>[EmailAddress]</c>, y heredarla arrastraría la
/// validación que se quiere quitar.</para>
/// </summary>
public class IgasConsoleForgotPasswordModel : AccountPageModel
{
    [Required]
    [StringLength(256)]
    [BindProperty]
    public string UserNameOrEmail { get; set; } = string.Empty;

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [HiddenInput]
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrlHash { get; set; }

    private readonly IAccountEmailer _accountEmailer;

    public IgasConsoleForgotPasswordModel(IAccountEmailer accountEmailer)
    {
        _accountEmailer = accountEmailer;
    }

    public virtual Task<IActionResult> OnGetAsync()
    {
        return Task.FromResult<IActionResult>(Page());
    }

    public virtual async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var entrada = UserNameOrEmail.Trim();

        // Por usuario primero y por correo después: en esta aplicación el correo es solo
        // un identificador (el enlace va por Telegram), así que lo natural es que la
        // gente escriba su usuario.
        var user = await UserManager.FindByNameAsync(entrada)
                   ?? await UserManager.FindByEmailAsync(entrada);

        if (user == null)
        {
            return EnlaceEnviado();
        }

        try
        {
            // Mismo camino que AccountAppService.SendPasswordResetCodeAsync, pero sin
            // pasar por su DTO, que exige un correo con formato válido.
            var resetToken = await UserManager.GeneratePasswordResetTokenAsync(user);

            await _accountEmailer.SendPasswordResetLinkAsync(
                user, resetToken, "MVC", ReturnUrl, ReturnUrlHash);
        }
        catch (UserFriendlyException e)
        {
            Alerts.Danger(GetLocalizeExceptionMessage(e));
            return Page();
        }

        return EnlaceEnviado();
    }

    private IActionResult EnlaceEnviado()
    {
        return RedirectToPage("./PasswordResetLinkSent", new
        {
            returnUrl = ReturnUrl,
            returnUrlHash = ReturnUrlHash
        });
    }
}
