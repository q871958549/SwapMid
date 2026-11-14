using SwapMid.SystemManagement;
using Xunit;

namespace SwapMid.EntityFrameworkCore.SystemManagement;

[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class CodeRuleEntityTypeProviderTests
    : CodeRuleEntityTypeProviderTests<SwapMidEntityFrameworkCoreTestModule>
{
}
