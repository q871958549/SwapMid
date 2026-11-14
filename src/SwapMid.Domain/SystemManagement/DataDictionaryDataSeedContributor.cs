using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace SwapMid.SystemManagement;

public class DataDictionaryDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<DataDictionary, Guid> _dictionaryRepository;
    private readonly IRepository<DataDictionaryItem, Guid> _itemRepository;
    private readonly IGuidGenerator _guidGenerator;

    public DataDictionaryDataSeedContributor(
        IRepository<DataDictionary, Guid> dictionaryRepository,
        IRepository<DataDictionaryItem, Guid> itemRepository,
        IGuidGenerator guidGenerator)
    {
        _dictionaryRepository = dictionaryRepository;
        _itemRepository = itemRepository;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public async Task SeedAsync(DataSeedContext context)
    {
        var specs = new List<DictSpec>
        {
            new("gender", "性别", "人员性别标准编码", new List<ItemSpec>
            {
                new("男", "male", 1),
                new("女", "female", 2),
                new("未知", "unknown", 3),
            }),
            new("yes_no", "是否", "通用布尔显示文案", new List<ItemSpec>
            {
                new("是", "true", 1),
                new("否", "false", 2),
            }),
            new("integration_env", "集成环境", "数据源与路由环境标识", new List<ItemSpec>
            {
                new("生产环境", "prod", 1),
                new("预发布环境", "staging", 2),
                new("开发测试环境", "dev", 3),
            }),
            new("webhook_provider", "Webhook 渠道类型", "机器人与通用 Webhook 协议格式", new List<ItemSpec>
            {
                new("通用 JSON Webhook", "generic", 1),
                new("Slack Incoming Webhook", "slack", 2),
                new("飞书 / Lark 群机器人", "feishu", 3),
                new("钉钉群机器人", "dingtalk", 4),
                new("企业微信群机器人", "wecom", 5),
            }),
        };

        var existingDicts = await _dictionaryRepository.GetListAsync();
        var dictByCode = existingDicts.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var spec in specs)
        {
            if (!dictByCode.TryGetValue(spec.Code, out var dict))
            {
                dict = new DataDictionary(
                    _guidGenerator.Create(),
                    spec.Code,
                    spec.Name,
                    spec.Description);
                await _dictionaryRepository.InsertAsync(dict, autoSave: true);
                dictByCode[spec.Code] = dict;
            }

            var existingItems = await _itemRepository.GetListAsync(i => i.DictionaryId == dict.Id);
            var existingValues = new HashSet<string>(existingItems.Select(i => i.Value), StringComparer.Ordinal);

            foreach (var itemSpec in spec.Items)
            {
                if (existingValues.Contains(itemSpec.Value))
                {
                    continue;
                }

                var item = new DataDictionaryItem(
                    _guidGenerator.Create(),
                    dict.Id,
                    itemSpec.Label,
                    itemSpec.Value,
                    itemSpec.SortOrder,
                    itemSpec.Description);
                await _itemRepository.InsertAsync(item, autoSave: true);
            }
        }
    }

    private sealed record DictSpec(
        string Code,
        string Name,
        string? Description,
        List<ItemSpec> Items);

    private sealed record ItemSpec(
        string Label,
        string Value,
        int SortOrder,
        string? Description = null);
}
