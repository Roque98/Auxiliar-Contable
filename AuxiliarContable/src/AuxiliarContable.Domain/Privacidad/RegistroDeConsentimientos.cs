using System;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace AuxiliarContable.Privacidad;

/// <summary>Quién aceptó la versión vigente del aviso de privacidad.</summary>
public class RegistroDeConsentimientos : DomainService
{
    private readonly IRepository<ConsentimientoDePrivacidad, Guid> _consentimientos;

    public RegistroDeConsentimientos(IRepository<ConsentimientoDePrivacidad, Guid> consentimientos)
    {
        _consentimientos = consentimientos;
    }

    public Task<bool> AceptoLaVigenteAsync(Guid userId) =>
        _consentimientos.AnyAsync(c => c.UserId == userId && c.Version == AvisoDePrivacidadConsts.VersionVigente);

    /// <summary>
    /// Registra la aceptación de <paramref name="versionLeida"/>, que es la que la pantalla
    /// le enseñó. Si no es la vigente (el aviso cambió mientras tenía la página abierta), se
    /// rechaza: aceptaría un texto que no leyó. Aceptar dos veces la misma no duplica.
    /// </summary>
    public async Task AceptarAsync(Guid userId, string? versionLeida)
    {
        if (versionLeida != AvisoDePrivacidadConsts.VersionVigente)
        {
            throw new BusinessException("AuxiliarContable:AvisoDePrivacidadDesactualizado");
        }

        if (await AceptoLaVigenteAsync(userId))
        {
            return;
        }

        await _consentimientos.InsertAsync(
            new ConsentimientoDePrivacidad(GuidGenerator.Create(), userId, AvisoDePrivacidadConsts.VersionVigente, Clock.Now),
            autoSave: true);
    }
}
