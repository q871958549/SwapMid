using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.MessageBus;

[Authorize(SwapMidPermissions.MessageBus.Subscriptions.Default)]
public class MessageSubscriptionAppService :
    CrudAppService<MessageSubscription, MessageSubscriptionDto, Guid, GetMessageSubscriptionListInput, MessageSubscriptionCreateUpdateDto>,
    IMessageSubscriptionAppService
{
    private readonly IRepository<MessageTopic, Guid> _topicRepository;
    private readonly IMessageDeliveryEndpointPolicy _endpointPolicy;

    public MessageSubscriptionAppService(
        IRepository<MessageSubscription, Guid> repository,
        IRepository<MessageTopic, Guid> topicRepository,
        IMessageDeliveryEndpointPolicy endpointPolicy)
        : base(repository)
    {
        _topicRepository = topicRepository;
        _endpointPolicy = endpointPolicy;
        GetPolicyName = SwapMidPermissions.MessageBus.Subscriptions.Default;
        GetListPolicyName = SwapMidPermissions.MessageBus.Subscriptions.Default;
        CreatePolicyName = SwapMidPermissions.MessageBus.Subscriptions.Create;
        UpdatePolicyName = SwapMidPermissions.MessageBus.Subscriptions.Update;
        DeletePolicyName = SwapMidPermissions.MessageBus.Subscriptions.Delete;
    }

    public override async Task<PagedResultDto<MessageSubscriptionDto>> GetListAsync(GetMessageSubscriptionListInput input)
    {
        var query = await Repository.GetQueryableAsync();
        if (input.TopicId.HasValue) query = query.Where(x => x.TopicId == input.TopicId.Value);
        if (!string.IsNullOrWhiteSpace(input.Filter)) query = query.Where(x => x.Name.Contains(input.Filter));
        query = query.ApplyTimeRange(input.StartTime, input.EndTime, input.TimeType);
        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            query.ApplyWhitelistedSorting(input.Sorting, SortWhitelist, "name").Skip(input.SkipCount).Take(input.MaxResultCount));

        var topicIds = items.Select(x => x.TopicId).Distinct().ToList();
        var topics = topicIds.Any()
            ? (await _topicRepository.GetListAsync(x => topicIds.Contains(x.Id))).ToDictionary(x => x.Id, x => x.Name)
            : new Dictionary<Guid, string>();

        var dtos = items.Select(s => { var d = s.ToDto(); d.HasSecret = !string.IsNullOrEmpty(s.Secret); topics.TryGetValue(s.TopicId, out var n); d.TopicName = n; return d; }).ToList();
        return new PagedResultDto<MessageSubscriptionDto>(totalCount, dtos);
    }

    // 服务端排序白名单（列 id 小写 → 排序委托）：未命中回退订阅名称，杜绝把用户串裸传动态排序（排序注入）。
    // TopicName 是分页之后按 TopicId 批量查字典回填的展示字段，非持久化列，SQL 层不存在、无法参与库内
    // 排序，不进白名单；前端对应列已禁排序。
    private static readonly IReadOnlyDictionary<string, Func<IQueryable<MessageSubscription>, bool, IOrderedQueryable<MessageSubscription>>> SortWhitelist =
        new Dictionary<string, Func<IQueryable<MessageSubscription>, bool, IOrderedQueryable<MessageSubscription>>>
        {
            ["name"] = (q, d) => d ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name),
            ["deliverytype"] = (q, d) => d ? q.OrderByDescending(x => x.DeliveryType) : q.OrderBy(x => x.DeliveryType),
            ["isenabled"] = (q, d) => d ? q.OrderByDescending(x => x.IsEnabled) : q.OrderBy(x => x.IsEnabled),
            ["creationtime"] = (q, d) => d ? q.OrderByDescending(x => x.CreationTime) : q.OrderBy(x => x.CreationTime),
        };

    public override async Task<MessageSubscriptionDto> GetAsync(Guid id)
    {
        var sub = await Repository.GetAsync(id);
        var dto = sub.ToDto();
        dto.HasSecret = !string.IsNullOrEmpty(sub.Secret);
        var topic = await _topicRepository.FindAsync(sub.TopicId);
        dto.TopicName = topic?.Name;
        return dto;
    }

    public override async Task<MessageSubscriptionDto> CreateAsync(MessageSubscriptionCreateUpdateDto input)
    {
        ValidateEndpoint(input.DeliveryType, input.EndpointUrl);
        var sub = new MessageSubscription(GuidGenerator.Create(), input.TopicId, input.Name, input.DeliveryType,
            input.EndpointUrl, input.ExchangeName, input.RoutingKey, input.FilterRule, input.Secret);
        if (!input.IsEnabled) sub.Disable();
        await Repository.InsertAsync(sub, autoSave: true);
        var dto = sub.ToDto();
        dto.HasSecret = !string.IsNullOrEmpty(sub.Secret);
        return dto;
    }

    public override async Task<MessageSubscriptionDto> UpdateAsync(Guid id, MessageSubscriptionCreateUpdateDto input)
    {
        ValidateEndpoint(input.DeliveryType, input.EndpointUrl);
        var sub = await Repository.GetAsync(id);
        // 密钥留空 = 保留原值(写不可读:前端不回显密钥,空提交不应清空已配置的密钥)。
        var secret = string.IsNullOrEmpty(input.Secret) ? sub.Secret : input.Secret;
        sub.Update(input.Name, input.DeliveryType, input.EndpointUrl, input.ExchangeName, input.RoutingKey, input.FilterRule, secret);
        if (input.IsEnabled) sub.Enable(); else sub.Disable();
        await Repository.UpdateAsync(sub, autoSave: true);
        var dto = sub.ToDto();
        dto.HasSecret = !string.IsNullOrEmpty(sub.Secret);
        return dto;
    }

    [Authorize(SwapMidPermissions.MessageBus.Subscriptions.Delete)]
    public override Task DeleteAsync(Guid id) => base.DeleteAsync(id);

    private void ValidateEndpoint(DeliveryType deliveryType, string? endpointUrl)
    {
        if (deliveryType != DeliveryType.RabbitMq)
        {
            _endpointPolicy.Validate(deliveryType, endpointUrl);
        }
    }
}
