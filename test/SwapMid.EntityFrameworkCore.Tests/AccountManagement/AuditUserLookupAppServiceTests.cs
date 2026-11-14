using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace SwapMid.AccountManagement;

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class AuditUserLookupAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IAuditUserLookupAppService _lookupAppService;
    private readonly IAccountUserAppService _accountUserAppService;

    public AuditUserLookupAppServiceTests()
    {
        _lookupAppService = GetRequiredService<IAuditUserLookupAppService>();
        _accountUserAppService = GetRequiredService<IAccountUserAppService>();
    }

    [Fact]
    public async Task LookupAsync_Should_Deduplicate_Omit_Missing_And_Apply_DisplayName_Fallback()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var namedUser = await CreateUserAsync(
            $"audit-named-{suffix}",
            name: "审计",
            surname: "用户");
        var fallbackUserName = $"audit-fallback-{suffix}";
        var fallbackUser = await CreateUserAsync(fallbackUserName);

        var result = await _lookupAppService.LookupAsync(new AuditUserLookupInput
        {
            Ids =
            [
                namedUser.Id,
                fallbackUser.Id,
                namedUser.Id,
                Guid.NewGuid(),
            ],
        });

        result.Items.Select(item => item.Id).ShouldBe([namedUser.Id, fallbackUser.Id]);
        result.Items[0].DisplayName.ShouldBe("审计 用户");
        result.Items[0].UserName.ShouldBe($"audit-named-{suffix}");
        result.Items[1].DisplayName.ShouldBe(fallbackUserName);
        result.Items[1].UserName.ShouldBe(fallbackUserName);
    }

    [Fact]
    public async Task LookupAsync_Should_Return_Empty_For_Empty_Input()
    {
        var result = await _lookupAppService.LookupAsync(new AuditUserLookupInput());

        result.Items.ShouldBeEmpty();
    }

    private Task<AccountUserDto> CreateUserAsync(
        string userName,
        string? name = null,
        string? surname = null)
        => _accountUserAppService.CreateAsync(new AccountUserCreateDto
        {
            UserName = userName,
            Name = name,
            Surname = surname,
            Email = $"{userName}@example.test",
            Password = "Han123!!",
            IsActive = true,
        });
}
