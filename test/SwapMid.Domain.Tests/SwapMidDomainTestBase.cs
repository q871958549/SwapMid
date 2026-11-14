using Volo.Abp.Modularity;

namespace SwapMid;

/* Inherit from this class for your domain layer tests. */
public abstract class SwapMidDomainTestBase<TStartupModule> : SwapMidTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
