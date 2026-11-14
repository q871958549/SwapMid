using Shouldly;
using Xunit;

namespace SwapMid.AccountManagement;

public class AccountManagementConstsTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(1000, 200)]
    [InlineData(5000, 200)]
    public void ClampListPageSize_Should_Cap_At_ListMaxPageSize(int requested, int expected)
    {
        AccountManagementConsts.Users.ListMaxPageSize.ShouldBe(200);
        AccountManagementConsts.Users.ClampListPageSize(requested).ShouldBe(expected);
    }
}
