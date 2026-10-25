using AuxiliarContable.Localization;
using Volo.Abp.Application.Services;

namespace AuxiliarContable;

/* Inherit your application services from this class.
 */
public abstract class AuxiliarContableAppService : ApplicationService
{
    protected AuxiliarContableAppService()
    {
        LocalizationResource = typeof(AuxiliarContableResource);
    }
}
