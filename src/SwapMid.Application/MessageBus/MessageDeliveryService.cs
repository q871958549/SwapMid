using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Monitoring;
using SwapMid.Notification;
using Microsoft.Extensions.Logging;
using Prometheus;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace SwapMid.MessageBus;

public class MessageDeliveryService : IMessageDeliveryService, ITransientDependency
{
    private readonly IRepository<MessageSubscription, Guid> _subscriptionRepository;
    private readonly IRepository<MessageDeliveryAttempt, Guid> _attemptRepository;
    private readonly IReadOnlyDictionary<DeliveryType, IMessageDeliveryClient> _deliveryClients;
    private readonly INotificationService _notificationService;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<MessageDeliveryService> _logger;

    public MessageDeliveryService(
        IRepository<MessageSubscription, Guid> subscriptionRepository,
        IRepository<MessageDeliveryAttempt, Guid> attemptRepository,
        IEnumerable<IMessageDeliveryClient> deliveryClients,
        INotificationService notificationService,
        IGuidGenerator guidGenerator,
        ILogger<MessageDeliveryService> logger)
    {
        _subscriptionRepository = subscriptionRepository;
        _attemptRepository = attemptRepository;
        _deliveryClients = deliveryClients.ToDictionary(c => c.DeliveryType);
        _notificationService = notificationService;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    // 消息进入死信（彻底投递失败）时告警运维——这条线 Hangfire 作业本身是成功的，不被作业失败过滤器覆盖。
    private Task NotifyDeadLetterAsync(MessageRecord record, string subscriptionName, string reason)
        => _notificationService.SendAsync(new NotificationSendRequest
        {
            Title = $"消息进入死信：{record.Subject}",
            Content = $"订阅：{subscriptionName}\n原因：{reason}",
            Level = NotificationLevel.Warning,
            Source = NotificationSources.MessageBus,
        });

    public async Task DeliverAsync(MessageRecord record, MessageTopic topic, CancellationToken cancellationToken = default)
    {
        // attempt 集合就是消息发布时的受众快照。只在快照尚未初始化时建立；
        // record 上的显式标记负责表达“已初始化但零匹配订阅”，不能仅靠 attempts.Count 判断。
        // 重试期间不得吸收后来新增/启用/改过滤后才匹配的订阅，也不得让 Queue 隐式切换接收方。
        var attempts = await _attemptRepository.GetListAsync(
            x => x.RecordId == record.Id, cancellationToken: cancellationToken);
        if (!record.IsAudienceSnapshotInitialized)
        {
            // 存量已有 attempt 的记录直接晋升为“已初始化”；没有 attempt 才真正计算首批受众。
            if (attempts.Count == 0)
            {
                var subscriptions = await _subscriptionRepository.GetListAsync(
                    x => x.TopicId == topic.Id && x.IsEnabled, cancellationToken: cancellationToken);
                var initialTargets = subscriptions
                    .Where(x => MatchesFilter(x, record))
                    .OrderBy(x => x.Name)
                    .ToList();

                if (topic.Type == TopicType.Queue)
                {
                    initialTargets = initialTargets.Take(1).ToList();
                }

                foreach (var subscription in initialTargets)
                {
                    var attempt = new MessageDeliveryAttempt(
                        _guidGenerator.Create(), record.Id, subscription.Id, subscription.Name);
                    await _attemptRepository.InsertAsync(attempt, autoSave: true);
                    attempts.Add(attempt);
                }
            }

            record.MarkAudienceSnapshotInitialized();
        }

        var maxAttempts = Math.Max(1, topic.RetryCount + 1);
        var pendingAttempts = attempts.Where(a => a.IsPendingDelivery).ToList();
        var subscriptionIds = pendingAttempts.Select(a => a.SubscriptionId).Distinct().ToList();
        var subscriptionsById = subscriptionIds.Count == 0
            ? new Dictionary<Guid, MessageSubscription>()
            : (await _subscriptionRepository.GetListAsync(
                    x => subscriptionIds.Contains(x.Id), cancellationToken: cancellationToken))
                .ToDictionary(x => x.Id);

        // 只投未成功（Pending/Failed）的 attempt——已成功订阅绝不重复投递，也不阻塞其他订阅（修缺陷A）。
        foreach (var attempt in pendingAttempts)
        {
            if (!subscriptionsById.TryGetValue(attempt.SubscriptionId, out var subscription))
            {
                await TerminalizeIneligibleAttemptAsync(record, attempt, "deleted");
                continue;
            }

            var ineligibleReason = GetIneligibleReason(subscription, record, topic);
            if (ineligibleReason != null)
            {
                await TerminalizeIneligibleAttemptAsync(record, attempt, ineligibleReason);
                continue;
            }

            await DeliverAttemptAsync(record, topic, subscription, attempt, maxAttempts, cancellationToken);
            await _attemptRepository.UpdateAsync(attempt, autoSave: true);
        }

        ApplyAggregateStatus(record, attempts);
    }

    private async Task TerminalizeIneligibleAttemptAsync(
        MessageRecord record,
        MessageDeliveryAttempt attempt,
        string reason)
    {
        attempt.MarkConfigDeadLettered(
            $"Subscription '{attempt.SubscriptionName}' is no longer eligible for this message ({reason}); automatic retries stopped.");
        await _attemptRepository.UpdateAsync(attempt, autoSave: true);
        PlatformMetrics.MessageDeliveryAbandonedTotal.WithLabels(reason).Inc();
        _logger.LogWarning(
            "Message delivery attempt stopped because its subscription is no longer eligible. RecordId={RecordId}, SubscriptionId={SubscriptionId}, Reason={Reason}",
            record.Id, attempt.SubscriptionId, reason);
    }

    private static string? GetIneligibleReason(
        MessageSubscription subscription,
        MessageRecord record,
        MessageTopic topic)
    {
        if (subscription.TopicId != topic.Id)
        {
            return "topic_mismatch";
        }

        if (!subscription.IsEnabled)
        {
            return "disabled";
        }

        return MatchesFilter(subscription, record) ? null : "filter_mismatch";
    }

    private async Task DeliverAttemptAsync(
        MessageRecord record, MessageTopic topic, MessageSubscription subscription,
        MessageDeliveryAttempt attempt, int maxAttempts, CancellationToken cancellationToken)
    {
        // 关键链路 span：投递类型/订阅/主题（RabbitMq 分支的原生 publisher span 经 AddSource 自动挂为子 span 并注入 traceparent）。
        using var activity = SwapMidTracing.StartCritical("message.deliver");
        activity?.SetTag("message.delivery_type", subscription.DeliveryType.ToString());
        activity?.SetTag("message.subscription", subscription.Name);
        activity?.SetTag("message.topic", topic.Name);

        // 按订阅投递类型路由到对应客户端（Http / RabbitMq / WeWorkBot）。
        if (!_deliveryClients.TryGetValue(subscription.DeliveryType, out var client))
        {
            // 配置型错误，重试无意义，直接死信。
            attempt.MarkConfigDeadLettered(
                $"Subscription '{subscription.Name}' uses unsupported delivery type {subscription.DeliveryType}");
            await NotifyDeadLetterAsync(record, subscription.Name, $"投递类型 {subscription.DeliveryType} 不受支持（配置错）");
            PlatformMetrics.MessageDeliveryTotal.WithLabels("failure").Inc();
            activity?.SetStatus(ActivityStatusCode.Error, $"unsupported delivery type {subscription.DeliveryType}");
            return;
        }

        using var deliveryTimer = PlatformMetrics.MessageDeliveryDuration.NewTimer();
        var response = await client.DeliverAsync(new MessageDeliveryRequest
        {
            MessageId = record.Id,
            Secret = subscription.Secret,
            EndpointUrl = subscription.EndpointUrl,
            ExchangeName = subscription.ExchangeName,
            RoutingKey = subscription.RoutingKey,
            TopicName = topic.Name,
            Subject = record.Subject,
            Payload = record.Payload,
            SubscriptionName = subscription.Name
        }, cancellationToken);

        if (response.Succeeded)
        {
            attempt.MarkDelivered();
            PlatformMetrics.MessageDeliveryTotal.WithLabels("success").Inc();
            return;
        }

        PlatformMetrics.MessageDeliveryTotal.WithLabels("failure").Inc();
        activity?.SetStatus(ActivityStatusCode.Error, response.ErrorMessage);
        var attemptNumber = attempt.RetryCount + 1;
        attempt.MarkFailed(BuildErrorMessage(subscription, attemptNumber, maxAttempts, response.ErrorMessage), maxAttempts);
        if (attempt.Status == MessageStatus.DeadLettered)
        {
            await NotifyDeadLetterAsync(record, subscription.Name, $"重试 {maxAttempts} 次仍失败：{attempt.LastError}");
        }
    }

    // 聚合各 attempt 明细 → record 整体状态（作业据此决定是否继续重投：Failed=继续，Delivered/DeadLettered=终态）。
    private static void ApplyAggregateStatus(MessageRecord record, List<MessageDeliveryAttempt> attempts)
    {
        if (attempts.Count == 0)
        {
            // 无匹配订阅：落痕即达（局限B）。
            record.SetAggregateStatus(MessageStatus.Delivered, 0, null);
            return;
        }

        var maxRetry = attempts.Max(a => a.RetryCount);
        if (attempts.All(a => a.Status == MessageStatus.Delivered))
        {
            record.SetAggregateStatus(MessageStatus.Delivered, maxRetry, null);
        }
        else if (attempts.Any(a => a.IsPendingDelivery))
        {
            record.SetAggregateStatus(MessageStatus.Failed, maxRetry, FirstUnsettledError(attempts));
        }
        else
        {
            record.SetAggregateStatus(MessageStatus.DeadLettered, maxRetry, FirstUnsettledError(attempts));
        }
    }

    private static string? FirstUnsettledError(List<MessageDeliveryAttempt> attempts)
        => attempts.Where(a => a.Status != MessageStatus.Delivered)
            .Select(a => a.LastError)
            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

    private static bool MatchesFilter(MessageSubscription subscription, MessageRecord record)
    {
        if (string.IsNullOrWhiteSpace(subscription.FilterRule))
        {
            return true;
        }

        return Contains(record.Subject, subscription.FilterRule) || Contains(record.Payload, subscription.FilterRule);
    }

    private static bool Contains(string value, string filter)
    {
        return value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildErrorMessage(MessageSubscription subscription, int attempt, int maxAttempts, string? errorMessage)
    {
        var message = string.IsNullOrWhiteSpace(errorMessage) ? "Delivery failed" : errorMessage;
        return $"Subscription '{subscription.Name}' failed on attempt {attempt}/{maxAttempts}: {message}";
    }
}
