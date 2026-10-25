using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AuxiliarContable.Privacidad;

/// <summary>
/// El aviso de privacidad para quien ya tiene cuenta: si aceptó la versión vigente y
/// aceptarla. Quien entra por invitación lo acepta al crear la cuenta
/// (<c>AceptarInvitacionDto.VersionDelAvisoAceptada</c>); esto es para todos los demás y
/// para cuando el aviso cambia.
/// </summary>
public interface IPrivacidadAppService : IApplicationService
{
    Task<EstadoDePrivacidadDto> GetEstadoAsync();

    Task AceptarAsync(AceptarAvisoDePrivacidadDto input);
}

public class EstadoDePrivacidadDto
{
    public string VersionVigente { get; set; } = string.Empty;

    /// <summary>¿Aceptó ESTA versión? Si no, la consola le pide aceptarla antes de seguir.</summary>
    public bool Aceptada { get; set; }
}

public class AceptarAvisoDePrivacidadDto
{
    /// <summary>La versión que la pantalla le enseñó. Si ya no es la vigente, se rechaza.</summary>
    [Required]
    [StringLength(AvisoDePrivacidadConsts.MaxVersionLength)]
    public string Version { get; set; } = string.Empty;
}
