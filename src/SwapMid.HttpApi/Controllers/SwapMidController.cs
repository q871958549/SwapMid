using SwapMid.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class SwapMidController : AbpControllerBase
{
    protected SwapMidController()
    {
        LocalizationResource = typeof(SwapMidResource);
    }
}
