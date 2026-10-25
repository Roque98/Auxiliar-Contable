using System.Threading.Tasks;
using AuxiliarContable.Telegram;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.DependencyInjection;
// ABP y ASP.NET Core definen cada uno su IdentityUser. El de aquí es el de ABP: es el que
// usa IdentityUserManager y el que lleva TenantId.
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AuxiliarContable.HealthChecks;

/// <summary>
/// Proveedor de segundo factor que entrega el código por Telegram.
///
/// <para>Hereda de <see cref="TotpSecurityStampBasedTokenProvider{TUser}"/> —el mismo que
/// usan por dentro los proveedores de correo y SMS de ASP.NET Identity— y por tanto NO
/// guarda nada: el código se deriva del sello de seguridad del usuario y de la hora, así
/// que se puede validar sin haberlo almacenado. Eso evita una tabla de códigos pendientes
/// y, de paso, que un código quede escrito en la base esperando a que alguien lo lea.</para>
///
/// <para>Consecuencia de ir atado al sello de seguridad: cambiar la contraseña o
/// desvincular invalidan al instante los códigos que hubiera en circulación.</para>
///
/// <para>Esta clase solo decide SI se puede usar el canal. Quien manda el mensaje es la
/// página de segundo factor: Identity genera y valida, pero la entrega es cosa de la
/// aplicación.</para>
/// </summary>
public class TelegramTwoFactorTokenProvider : TotpSecurityStampBasedTokenProvider<IdentityUser>, ITransientDependency
{
    private readonly ITelegramLinkRepository _vinculos;

    public TelegramTwoFactorTokenProvider(ITelegramLinkRepository vinculos)
    {
        _vinculos = vinculos;
    }

    /// <summary>
    /// Sin Telegram vinculado no hay por dónde entregar el código, así que este canal no
    /// está disponible.
    /// <para>⚠️ Ojo con lo que implica: si un usuario tiene el segundo factor activado y
    /// desvincula Telegram, Identity se queda sin proveedores válidos y deja de pedir el
    /// segundo factor — entra directo con la contraseña, sin avisar. Por eso desvincular
    /// APAGA el segundo factor de forma explícita (ver <c>TelegramLinkAppService</c>): más
    /// vale decirlo que degradar la seguridad en silencio.</para>
    /// </summary>
    public override async Task<bool> CanGenerateTwoFactorTokenAsync(
        UserManager<IdentityUser> manager, IdentityUser user)
    {
        var vinculo = await _vinculos.FindByUserIdAsync(user.Id);
        return vinculo?.ChatId != null;
    }

    /// <summary>
    /// Entra en el cálculo del código para que un código generado para este canal no valga
    /// en otro (ni para otro propósito, ni para otro usuario).
    /// </summary>
    public override Task<string> GetUserModifierAsync(
        string purpose, UserManager<IdentityUser> manager, IdentityUser user)
    {
        return Task.FromResult($"{TelegramTwoFactorConsts.ProviderName}:{purpose}:{user.Id}");
    }
}
