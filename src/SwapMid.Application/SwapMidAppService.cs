using SwapMid.Localization;
using Volo.Abp.Application.Services;

namespace SwapMid;

/* Inherit your application services from this class.
 */
public abstract class SwapMidAppService : ApplicationService
{
    protected SwapMidAppService()
    {
        LocalizationResource = typeof(SwapMidResource);
    }
}
