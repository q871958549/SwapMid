using Microsoft.Extensions.Localization;
using SwapMid.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace SwapMid;

[Dependency(ReplaceServices = true)]
public class SwapMidBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<SwapMidResource> _localizer;

    public SwapMidBrandingProvider(IStringLocalizer<SwapMidResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
