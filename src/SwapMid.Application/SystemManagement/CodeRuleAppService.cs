using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.SystemManagement;

[Authorize(SwapMidPermissions.SystemManagement.CodeRules.Default)]
public class CodeRuleAppService : ApplicationService, ICodeRuleAppService
{
    private readonly IRepository<CodeRule, Guid> _repository;
    private readonly IEnumerable<ICodeRuleEntityTypeProvider> _entityTypeProviders;
    private readonly CodeRuleManager _codeRuleManager;

    public CodeRuleAppService(
        IRepository<CodeRule, Guid> repository,
        IEnumerable<ICodeRuleEntityTypeProvider> entityTypeProviders,
        CodeRuleManager codeRuleManager)
    {
        _repository = repository;
        _entityTypeProviders = entityTypeProviders;
        _codeRuleManager = codeRuleManager;
    }

    public async Task<PagedResultDto<CodeRuleDto>> GetListAsync(GetCodeRuleListInput input)
    {
        var query = await _repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter;
            query = query.Where(x => x.EntityType.Contains(f) || x.DisplayName.Contains(f));
        }

        if (input.IsEnabled.HasValue)
        {
            query = query.Where(x => x.IsEnabled == input.IsEnabled.Value);
        }

        var total = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "entitytype", logger: Logger)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<CodeRuleDto>(total, items.Select(ToDto).ToList());
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）。此前硬编码 OrderBy(EntityType) 完全忽略 input.Sorting。
    // 默认序保持实体类型升序（结构序）不变。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<CodeRule>, bool, IOrderedQueryable<CodeRule>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<CodeRule>, bool, IOrderedQueryable<CodeRule>>>
        {
            ["entitytype"] = (q, d) => d ? q.OrderByDescending(x => x.EntityType) : q.OrderBy(x => x.EntityType),
            ["displayname"] = (q, d) => d ? q.OrderByDescending(x => x.DisplayName) : q.OrderBy(x => x.DisplayName),
            ["prefix"] = (q, d) => d ? q.OrderByDescending(x => x.Prefix) : q.OrderBy(x => x.Prefix),
            ["sequencepad"] = (q, d) => d ? q.OrderByDescending(x => x.SequencePad) : q.OrderBy(x => x.SequencePad),
            ["resetcycle"] = (q, d) => d ? q.OrderByDescending(x => x.ResetCycle) : q.OrderBy(x => x.ResetCycle),
            ["currentsequence"] = (q, d) => d ? q.OrderByDescending(x => x.CurrentSequence) : q.OrderBy(x => x.CurrentSequence),
            ["isenabled"] = (q, d) => d ? q.OrderByDescending(x => x.IsEnabled) : q.OrderBy(x => x.IsEnabled),
            ["description"] = (q, d) => d ? q.OrderByDescending(x => x.Description) : q.OrderBy(x => x.Description),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
            ["lastmodificationtime"] = (q, d) => d ? q.OrderByDescending(x => x.LastModificationTime) : q.OrderBy(x => x.LastModificationTime),
        };

    public async Task<CodeRuleDto> GetAsync(Guid id)
    {
        var rule = await _repository.GetAsync(id);
        return ToDto(rule);
    }

    [Authorize(SwapMidPermissions.SystemManagement.CodeRules.Create)]
    public async Task<CodeRuleDto> CreateAsync(CodeRuleCreateUpdateDto input)
    {
        var existing = await AsyncExecuter.FirstOrDefaultAsync((await _repository.GetQueryableAsync())
            .Where(x => x.EntityType == input.EntityType));
        if (existing != null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.SystemManagement.CodeRuleEntityTypeExists)
                .WithData("entityType", input.EntityType);
        }

        var rule = new CodeRule(
            GuidGenerator.Create(),
            input.EntityType,
            input.DisplayName,
            input.Prefix ?? string.Empty,
            input.Suffix,
            input.IncludeDate,
            input.DateFormat,
            input.SequencePad,
            input.ResetCycle,
            input.Description);

        if (!input.IsEnabled) rule.Disable();

        await _repository.InsertAsync(rule, autoSave: true);
        return ToDto(rule);
    }

    [Authorize(SwapMidPermissions.SystemManagement.CodeRules.Update)]
    public async Task<CodeRuleDto> UpdateAsync(Guid id, CodeRuleCreateUpdateDto input)
    {
        var rule = await _repository.GetAsync(id);

        var conflict = await AsyncExecuter.FirstOrDefaultAsync((await _repository.GetQueryableAsync())
            .Where(x => x.EntityType == input.EntityType && x.Id != id));
        if (conflict != null)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.SystemManagement.CodeRuleEntityTypeExists)
                .WithData("entityType", input.EntityType);
        }

        rule.Update(
            input.DisplayName,
            input.Prefix ?? string.Empty,
            input.Suffix,
            input.IncludeDate,
            input.DateFormat,
            input.SequencePad,
            input.ResetCycle,
            input.Description);

        if (input.IsEnabled) rule.Enable(); else rule.Disable();

        await _repository.UpdateAsync(rule, autoSave: true);
        return ToDto(rule);
    }

    [Authorize(SwapMidPermissions.SystemManagement.CodeRules.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id, autoSave: true);
    }

    /// <summary>
    /// 按实体类型生成下一个编码。生成逻辑已下沉至 <see cref="CodeRuleManager"/>（后台线程如 Hangfire 定时任务
    /// 无用户身份，接口注入的带权 AppService 会因类级 <c>[Authorize]</c> 门面必炸
    /// <see cref="Volo.Abp.Authorization.AbpAuthorizationException"/>，见蓝图 §17 缺陷记录）；
    /// 本方法只保留门面一行委托，鉴权面与行为均不变。
    /// </summary>
    public Task<string> GenerateAsync(string entityType) => _codeRuleManager.GenerateAsync(entityType);

    /// <summary>
    /// 聚合各域贡献者注册的可选业务对象（前端下拉选项 + 列表中文名映射的单一事实源）。
    /// 同 EntityType 多次注册取首个去重；按分组、中文名排序。同步聚合内存清单，无需 await 但保持 async 签名对齐契约。
    /// </summary>
    public Task<List<CodeRuleEntityTypeDto>> GetEntityTypesAsync()
    {
        var list = _entityTypeProviders
            .SelectMany(p => p.GetEntityTypes())
            .GroupBy(x => x.EntityType)
            .Select(g => g.First())
            .OrderBy(x => x.Group, StringComparer.Ordinal)
            .ThenBy(x => x.DisplayName, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(list);
    }

    private static CodeRuleDto ToDto(CodeRule rule) => new()
    {
        Id = rule.Id,
        EntityType = rule.EntityType,
        DisplayName = rule.DisplayName,
        Prefix = rule.Prefix,
        Suffix = rule.Suffix,
        IncludeDate = rule.IncludeDate,
        DateFormat = rule.DateFormat,
        SequencePad = rule.SequencePad,
        ResetCycle = rule.ResetCycle,
        CurrentSequence = rule.CurrentSequence,
        IsEnabled = rule.IsEnabled,
        Description = rule.Description,
        Sample = rule.GetSample(),
        CreationTime = rule.CreationTime,
        CreatorId = rule.CreatorId,
        LastModificationTime = rule.LastModificationTime,
        LastModifierId = rule.LastModifierId,
    };
}
