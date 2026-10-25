using IgasConsole.Localization;
using Volo.Abp.Application.Services;

namespace IgasConsole;

/* Inherit your application services from this class.
 */
public abstract class IgasConsoleAppService : ApplicationService
{
    protected IgasConsoleAppService()
    {
        LocalizationResource = typeof(IgasConsoleResource);
    }
}
