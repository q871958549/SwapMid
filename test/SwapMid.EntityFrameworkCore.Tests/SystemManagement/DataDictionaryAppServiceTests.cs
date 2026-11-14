using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.SystemManagement;

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class DataDictionaryAppServiceTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDataDictionaryAppService _service;

    public DataDictionaryAppServiceTests()
    {
        _service = GetRequiredService<IDataDictionaryAppService>();
    }

    [Fact]
    public async Task Should_Create_And_Filter_Dictionary()
    {
        var code = $"dict_{Guid.NewGuid():N}";

        var created = await _service.CreateAsync(new DataDictionaryCreateUpdateDto
        {
            Code = code,
            Name = "测试字典",
            Description = "desc",
            IsEnabled = true
        });

        created.Id.ShouldNotBe(Guid.Empty);
        created.Code.ShouldBe(code);
        created.IsEnabled.ShouldBeTrue();

        var list = await _service.GetListAsync(new GetDataDictionaryListInput
        {
            Filter = code,
            MaxResultCount = 10
        });

        list.TotalCount.ShouldBe(1);
        list.Items.Single().Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Code()
    {
        var code = $"dup_{Guid.NewGuid():N}";
        await _service.CreateAsync(new DataDictionaryCreateUpdateDto { Code = code, Name = "A", IsEnabled = true });

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            _service.CreateAsync(new DataDictionaryCreateUpdateDto { Code = code, Name = "B", IsEnabled = true }));

        ex.Code.ShouldBe(SwapMidDomainErrorCodes.SystemManagement.DataDictionaryCodeExists);
    }

    [Fact]
    public async Task Should_Return_Enabled_Items_By_Code_Ordered()
    {
        var code = $"gender_{Guid.NewGuid():N}";
        var dict = await _service.CreateAsync(new DataDictionaryCreateUpdateDto { Code = code, Name = "性别", IsEnabled = true });

        await _service.CreateItemAsync(new DataDictionaryItemCreateUpdateDto
        {
            DictionaryId = dict.Id, Label = "女", Value = "2", SortOrder = 2, IsEnabled = true
        });
        await _service.CreateItemAsync(new DataDictionaryItemCreateUpdateDto
        {
            DictionaryId = dict.Id, Label = "男", Value = "1", SortOrder = 1, IsEnabled = true
        });
        await _service.CreateItemAsync(new DataDictionaryItemCreateUpdateDto
        {
            DictionaryId = dict.Id, Label = "未知", Value = "0", SortOrder = 3, IsEnabled = false
        });

        var items = await _service.GetItemsByCodeAsync(code);

        items.Items.Select(x => x.Value).ToArray().ShouldBe(new[] { "1", "2" });
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Item_Value_In_Same_Dictionary()
    {
        var code = $"d_{Guid.NewGuid():N}";
        var dict = await _service.CreateAsync(new DataDictionaryCreateUpdateDto { Code = code, Name = "X", IsEnabled = true });

        await _service.CreateItemAsync(new DataDictionaryItemCreateUpdateDto
        {
            DictionaryId = dict.Id, Label = "甲", Value = "1", IsEnabled = true
        });

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            _service.CreateItemAsync(new DataDictionaryItemCreateUpdateDto
            {
                DictionaryId = dict.Id, Label = "乙", Value = "1", IsEnabled = true
            }));

        ex.Code.ShouldBe(SwapMidDomainErrorCodes.SystemManagement.DataDictionaryItemValueExists);
    }

    [Fact]
    public async Task Should_Cascade_Delete_Items_When_Dictionary_Deleted()
    {
        var code = $"del_{Guid.NewGuid():N}";
        var dict = await _service.CreateAsync(new DataDictionaryCreateUpdateDto { Code = code, Name = "Y", IsEnabled = true });
        await _service.CreateItemAsync(new DataDictionaryItemCreateUpdateDto
        {
            DictionaryId = dict.Id, Label = "项", Value = "1", IsEnabled = true
        });

        await _service.DeleteAsync(dict.Id);

        var items = await _service.GetItemsAsync(dict.Id);
        items.Items.ShouldBeEmpty();
    }
}
