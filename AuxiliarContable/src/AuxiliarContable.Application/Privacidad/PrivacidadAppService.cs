using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Users;

namespace AuxiliarContable.Privacidad;

/// <summary>
/// Ver <see cref="IPrivacidadAppService"/>. <b>Sin permiso propio</b>, como «Mi cuenta»: cada
/// uno acepta lo suyo y el usuario sale siempre de la sesión, nunca de la petición.
/// </summary>
[Authorize]
public class PrivacidadAppService : AuxiliarContableAppService, IPrivacidadAppService
{
    private readonly RegistroDeConsentimientos _consentimientos;

    public PrivacidadAppService(RegistroDeConsentimientos consentimientos)
    {
        _consentimientos = consentimientos;
    }

    public async Task<EstadoDePrivacidadDto> GetEstadoAsync()
    {
        return new EstadoDePrivacidadDto
        {
            VersionVigente = AvisoDePrivacidadConsts.VersionVigente,
            Aceptada = await _consentimientos.AceptoLaVigenteAsync(CurrentUser.GetId()),
        };
    }

    public Task AceptarAsync(AceptarAvisoDePrivacidadDto input)
    {
        return _consentimientos.AceptarAsync(CurrentUser.GetId(), input.Version);
    }
}
