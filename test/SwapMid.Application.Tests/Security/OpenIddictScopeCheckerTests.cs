using System.Collections.Generic;
using System.Security.Claims;
using Shouldly;
using Xunit;

namespace SwapMid.Security;

/// <summary>
/// 对外接口「二次 scope 门控」共用逻辑单元测试：仅授 <c>external-integration</c> 的第三方客户端凭据 Token
/// 不得含平台级 <c>SwapMid</c> scope，而内部平台 Token 必须含。
/// </summary>
public class OpenIddictScopeCheckerTests
{
    private static ClaimsPrincipal PrincipalWith(string claimType, string value)
        => new(new ClaimsIdentity(new List<Claim> { new(claimType, value) }, "TestAuth"));

    [Theory]
    [InlineData("scope")]
    [InlineData("oi_scp")]
    public void 内部平台token_含平台scope_HasPlatformScope为真(string claimType)
    {
        var principal = PrincipalWith(claimType, "SwapMid");

        principal.HasPlatformScope().ShouldBeTrue();
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("oi_scp")]
    public void 第三方窄token_仅external_integration_HasPlatformScope为假(string claimType)
    {
        var principal = PrincipalWith(claimType, OpenIddictScopeChecker.ExternalIntegrationScope);

        principal.HasPlatformScope().ShouldBeFalse();
        principal.HasExternalIntegrationScope().ShouldBeTrue();
        principal.HasScope(OpenIddictScopeChecker.ExternalIntegrationScope).ShouldBeTrue();
    }

    [Fact]
    public void 空格分隔多scope_逐个匹配()
    {
        var principal = PrincipalWith("scope", "external-integration SwapMid");

        principal.HasScope("external-integration").ShouldBeTrue();
        principal.HasScope("SwapMid").ShouldBeTrue();
        principal.HasScope("SwapMid2").ShouldBeFalse();
    }

    [Fact]
    public void 无scope声明_一律为假()
    {
        var principal = PrincipalWith("sub", "some-user-id");

        principal.HasPlatformScope().ShouldBeFalse();
        principal.HasExternalIntegrationScope().ShouldBeFalse();
    }

    [Fact]
    public void null主体_安全返回假()
    {
        ClaimsPrincipal? principal = null;

        principal.HasPlatformScope().ShouldBeFalse();
        principal.HasScope("external-integration").ShouldBeFalse();
    }
}
