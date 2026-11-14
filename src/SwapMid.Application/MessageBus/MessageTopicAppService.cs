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

namespace SwapMid.MessageBus;

[Authorize(SwapMidPermissions.MessageBus.Topics.Default)]
public class MessageTopicAppService :
    CrudAppService<MessageTopic, MessageTopicDto, Guid, GetMessageTopicListInput, MessageTopicCreateUpdateDto>,
    IMessageTopicAppService
{
    private readonly IMessageTopicRepository _topicRepository;

    public MessageTopicAppService(IRepository<MessageTopic, Guid> repository, IMessageTopicRepository topicRepository)
        : base(repository)
    {
        _topicRepository = topicRepository;
        GetPolicyName = SwapMidPermissions.MessageBus.Topics.Default;
        GetListPolicyName = SwapMidPermissions.MessageBus.Topics.Default;
        CreatePolicyName = SwapMidPermissions.MessageBus.Topics.Create;
        UpdatePolicyName = SwapMidPermissions.MessageBus.Topics.Update;
        DeletePolicyName = SwapMidPermissions.MessageBus.Topics.Delete;
    }

    public override async Task<PagedResultDto<MessageTopicDto>> GetListAsync(GetMessageTopicListInput input)
    {
        var query = await Repository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.Filter))
            query = query.Where(x => x.Name.Contains(input.Filter));
        if (input.Type.HasValue)
            query = query.Where(x => x.Type == input.Type.Value);
        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);
        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            query.ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "name").Skip(input.SkipCount).Take(input.MaxResultCount));
        return new PagedResultDto<MessageTopicDto>(totalCount, items.Select(x => x.ToDto()).ToList());
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）：未命中回退主题名称，杜绝把用户串裸传动态排序（排序注入）。
    // MessageTopic 是纯标量聚合根，前端展示列均可直接进白名单，无派生字段需要排除。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<MessageTopic>, bool, IOrderedQueryable<MessageTopic>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<MessageTopic>, bool, IOrderedQueryable<MessageTopic>>>
        {
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["type"] = (q, d) => d ? q.OrderByDescending(x => x.Type) : q.OrderBy(x => x.Type),
            ["retrycount"] = (q, d) => d ? q.OrderByDescending(x => x.RetryCount) : q.OrderBy(x => x.RetryCount),
            ["isenabled"] = (q, d) => d ? q.OrderByDescending(x => x.IsEnabled) : q.OrderBy(x => x.IsEnabled),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
        };

    public override async Task<MessageTopicDto> GetAsync(Guid id)
    {
        var topic = await Repository.GetAsync(id);
        return topic.ToDto();
    }

    public override async Task<MessageTopicDto> CreateAsync(MessageTopicCreateUpdateDto input)
    {
        if (await _topicRepository.NameExistsAsync(input.Name))
            throw new BusinessException(SwapMidDomainErrorCodes.MessageBus.TopicNameAlreadyExists).WithData("name", input.Name);
        var topic = new MessageTopic(GuidGenerator.Create(), input.Name, input.Type, input.Description, input.RetryCount, input.RetryIntervalSeconds);
        if (!input.IsEnabled) topic.Disable();
        await Repository.InsertAsync(topic, autoSave: true);
        return topic.ToDto();
    }

    public override async Task<MessageTopicDto> UpdateAsync(Guid id, MessageTopicCreateUpdateDto input)
    {
        var topic = await Repository.GetAsync(id);
        if (await _topicRepository.NameExistsAsync(input.Name, excludeId: id))
            throw new BusinessException(SwapMidDomainErrorCodes.MessageBus.TopicNameAlreadyExists).WithData("name", input.Name);
        topic.Update(input.Name, input.Type, input.Description, input.RetryCount, input.RetryIntervalSeconds);
        if (input.IsEnabled) topic.Enable(); else topic.Disable();
        await Repository.UpdateAsync(topic, autoSave: true);
        return topic.ToDto();
    }

    [Authorize(SwapMidPermissions.MessageBus.Topics.Delete)]
    public override Task DeleteAsync(Guid id) => base.DeleteAsync(id);
}
