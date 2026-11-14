using Volo.Abp.Modularity;

namespace SwapMid;

public abstract class SwapMidApplicationTestBase<TStartupModule> : SwapMidTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
