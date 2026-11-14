using Volo.Abp;
using Xunit;

namespace SwapMid.EntityFrameworkCore;

[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
public abstract class SwapMidEntityFrameworkCoreTestBase : SwapMidTestBase<SwapMidEntityFrameworkCoreTestModule>
{

}
