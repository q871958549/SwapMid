using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.ApiManagement;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class ApiGroupAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IApiGroupAppService _apiGroupAppService;

    public ApiGroupAppServiceTests()
    {
        _apiGroupAppService = GetRequiredService<IApiGroupAppService>();
    }

    [Fact]
    public async Task Should_Create_And_Query_Api_Group()
    {
        var createInput = new ApiGroupCreateUpdateDto
        {
            Name = $"Integration APIs {Guid.NewGuid():N}",
            Description = "Public integration endpoints",
            Order = 30,
            IsEnabled = true
        };

        var created = await _apiGroupAppService.CreateAsync(createInput);

        created.Id.ShouldNotBe(Guid.Empty);
        created.Name.ShouldBe(createInput.Name);
        created.Description.ShouldBe(createInput.Description);
        created.Order.ShouldBe(createInput.Order);
        created.IsEnabled.ShouldBeTrue();

        var list = await _apiGroupAppService.GetListAsync(new GetApiGroupListInput
        {
            Filter = "Integration APIs",
            MaxResultCount = 50
        });

        list.TotalCount.ShouldBeGreaterThanOrEqualTo(1);
        list.Items.ShouldContain(x => x.Id == created.Id);
    }

    [Fact]
    public async Task Should_Return_Api_Groups_In_Configured_Order()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await _apiGroupAppService.CreateAsync(new ApiGroupCreateUpdateDto
        {
            Name = $"Ordered B {suffix}",
            Order = 20
        });
        await _apiGroupAppService.CreateAsync(new ApiGroupCreateUpdateDto
        {
            Name = $"Ordered A {suffix}",
            Order = 10
        });

        var list = await _apiGroupAppService.GetListAsync(new GetApiGroupListInput
        {
            Filter = suffix,
            MaxResultCount = 10
        });

        list.Items.Count.ShouldBe(2);
        list.Items.Select(x => x.Name).ToArray().ShouldBe(new[]
        {
            $"Ordered A {suffix}",
            $"Ordered B {suffix}"
        });
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Api_Group_Name()
    {
        var name = $"Duplicate {Guid.NewGuid():N}";
        await _apiGroupAppService.CreateAsync(new ApiGroupCreateUpdateDto
        {
            Name = name
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _apiGroupAppService.CreateAsync(new ApiGroupCreateUpdateDto
            {
                Name = name
            }));

        exception.Code.ShouldBe(SwapMidDomainErrorCodes.ApiManagement.GroupNameAlreadyExists);
    }
}
