using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Emailing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using IdentityOptions = Microsoft.AspNetCore.Identity.IdentityOptions;

namespace AuxiliarContable.Identity;

/// <summary>
/// El servicio de cuenta de ABP, con un solo cambio: pedir el enlace de recuperación para
/// un correo que no existe responde igual que para uno que sí.
///
/// <para>ABP lanza «correo no válido» si no encuentra la cuenta (verificado en
/// <c>refs/abp</c>, <c>AccountAppService.GetUserByEmailAsync</c>). El endpoint
/// (<c>POST /api/account/send-password-reset-code</c>) es anónimo, así que con eso cualquiera
/// podía preguntar qué correos tienen cuenta. La página de «olvidé mi contraseña» ya se
/// comportaba así; esto cierra la misma puerta por la API.</para>
/// </summary>
// Sin controlador propio: ABP ya expone este servicio con el suyo (que recibe esta
// sustitución por inyección). Sin esto aparecía DUPLICADO en /api/app/auxiliar-*, y el de
// cuenta se saltaba el límite de peticiones de las rutas de credenciales.
[RemoteService(IsEnabled = false)]
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAccountAppService), typeof(AccountAppService), typeof(AuxiliarAccountAppService))]
public class AuxiliarAccountAppService : AccountAppService
{
    public AuxiliarAccountAppService(
        IdentityUserManager userManager,
        IIdentityRoleRepository roleRepository,
        IAccountEmailer accountEmailer,
        IdentitySecurityLogManager identitySecurityLogManager,
        IOptions<IdentityOptions> identityOptions)
        : base(userManager, roleRepository, accountEmailer, identitySecurityLogManager, identityOptions)
    {
    }

    public override async Task SendPasswordResetCodeAsync(SendPasswordResetCodeDto input)
    {
        var user = await UserManager.FindByEmailAsync(input.Email);
        if (user == null)
        {
            return;
        }

        var resetToken = await UserManager.GeneratePasswordResetTokenAsync(user);
        await AccountEmailer.SendPasswordResetLinkAsync(user, resetToken, input.AppName, input.ReturnUrl, input.ReturnUrlHash);
    }
}
