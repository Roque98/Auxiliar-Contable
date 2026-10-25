using IgasConsole.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace IgasConsole.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class IgasConsoleController : AbpControllerBase
{
    protected IgasConsoleController()
    {
        LocalizationResource = typeof(IgasConsoleResource);
    }
}
