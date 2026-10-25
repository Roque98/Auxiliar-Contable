using AuxiliarContable.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace AuxiliarContable.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class AuxiliarContableController : AbpControllerBase
{
    protected AuxiliarContableController()
    {
        LocalizationResource = typeof(AuxiliarContableResource);
    }
}
