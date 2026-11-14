using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Notification.Channels;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace SwapMid.Notification;

public class NotificationService : INotificationService, ITransientDependency
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly IRepository<NotificationLog, Guid> _logRepository;
    private readonly IRepository<NotificationRecipient, Guid> _recipientRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IEnumerable<INotificationChannel> channels,
        IRepository<NotificationLog, Guid> logRepository,
        IRepository<NotificationRecipient, Guid> recipientRepository,
        IGuidGenerator guidGenerator,
        IUnitOfWorkManager uowManager,
        ILogger<NotificationService> logger)
    {
        _channels = channels;
        _logRepository = logRepository;
        _recipientRepository = recipientRepository;
        _guidGenerator = guidGenerator;
        _uowManager = uowManager;
        _logger = logger;
    }

    public async Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
    {
        var targets = request.Channel.HasValue
            ? _channels.Where(c => c.ChannelType == request.Channel.Value)
            : _channels;

        var broadcast = string.IsNullOrWhiteSpace(request.ToUser);
        List<NotificationRecipient> enabledRecipients = [];
        List<NotificationRecipient> matched = [];
        if (broadcast)
        {
            enabledRecipients = await _recipientRepository.GetListAsync(r => r.IsEnabled, cancellationToken: cancellationToken);
            matched = enabledRecipients.Where(r => r.Matches(request.Level, request.Source)).ToList();
        }

        var attempted = false;
        var errors = new List<string>();
        foreach (var channel in targets)
        {
            if (!await channel.IsEnabledAsync())
            {
                continue;
            }

            var perRequest = request;
            if (broadcast)
            {
                var addresses = matched
                    .Select(r => r.AddressFor(channel.ChannelType))
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct()
                    .ToList();

                if (channel.ChannelType == NotificationChannelType.Email)
                {
                    if (addresses.Count == 0)
                    {
                        continue;
                    }

                    perRequest = CloneWithToUser(request, string.Join(",", addresses));
                }
                else
                {
                    // Webhook 渠道本身即群组端点：若系统中配置了启用的接收人但无一人订阅当前级别/来源，则跳过；
                    // 若尚未录入接收人或有接收人命中，则允许发往群 Webhook（如有 WebhookTarget 则合并作为 @提及）。
                    if (enabledRecipients.Count > 0 && matched.Count == 0)
                    {
                        continue;
                    }

                    if (addresses.Count > 0)
                    {
                        perRequest = CloneWithToUser(request, string.Join(",", addresses));
                    }
                }
            }

            attempted = true;
            var result = await channel.SendAsync(perRequest, cancellationToken);
            if (!result.Success)
            {
                errors.Add($"{channel.ChannelType}：{result.Error}");
            }

            await SaveLogAsync(channel.ChannelType, perRequest, result);
        }

        if (!attempted)
        {
            return NotificationSendResult.Skip(broadcast
                ? "没有已启用渠道、或没有匹配的接收人（检查渠道开关与接收人订阅配置）"
                : "没有已启用的通知渠道");
        }

        return errors.Count == 0
            ? NotificationSendResult.Ok()
            : NotificationSendResult.Fail(string.Join("；", errors));
    }

    private static NotificationSendRequest CloneWithToUser(NotificationSendRequest r, string toUser) => new()
    {
        Title = r.Title,
        Content = r.Content,
        Level = r.Level,
        ToUser = toUser,
        Source = r.Source,
        Channel = r.Channel,
        Attachments = r.Attachments,
        ActionPath = r.ActionPath,
        ActionLabel = r.ActionLabel
    };

    private async Task SaveLogAsync(NotificationChannelType channel, NotificationSendRequest request, NotificationSendResult result)
    {
        try
        {
            using var uow = _uowManager.Begin(requiresNew: true);
            await _logRepository.InsertAsync(new NotificationLog(
                _guidGenerator.Create(),
                channel,
                request.Title,
                request.ToUser,
                request.Level,
                request.Source,
                result.Success,
                result.Error), autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通知台账落库失败 title={Title}", request.Title);
        }
    }
}
