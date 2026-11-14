using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SwapMid.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Auditing;

namespace SwapMid.Notification;

[DisableAuditing]
public class AlertWebhookAppService : SwapMidAppService, IAlertWebhookAppService
{
    private readonly INotificationService _notificationService;

    public AlertWebhookAppService(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task<AlertWebhookReceiveResultDto> ReceiveAsync(string? token, AlertmanagerWebhookInput payload)
    {
        var expected = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationAlertWebhookToken);
        if (!FixedTimeEquals(expected, token))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.NotificationCenter.InvalidWebhookToken);
        }

        var alerts = payload?.Alerts;
        if (alerts == null || alerts.Count == 0)
        {
            return new AlertWebhookReceiveResultDto
            {
                Accepted = true,
                ReceivedCount = 0,
                FailedCount = 0,
                Message = "no alerts"
            };
        }

        var failures = 0;
        foreach (var alert in alerts)
        {
            var (title, content, level) = Format(alert);
            var result = await _notificationService.SendAsync(new NotificationSendRequest
            {
                Title = title,
                Content = content,
                Level = level,
                Source = NotificationSources.Alertmanager
            });

            if (!result.Success && !result.Skipped)
            {
                failures++;
                Logger.LogWarning("Alertmanager 告警转发失败：{Error}", result.Error);
            }
        }

        return new AlertWebhookReceiveResultDto
        {
            Accepted = failures == 0,
            ReceivedCount = alerts.Count,
            FailedCount = failures,
            Message = failures == 0
                ? $"processed {alerts.Count}"
                : $"{failures} of {alerts.Count} failed"
        };
    }

    internal static bool FixedTimeEquals(string? expected, string? provided)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static (string Title, string Content, NotificationLevel Level) Format(AlertmanagerAlertItemDto a)
    {
        var labels = a.Labels ?? new Dictionary<string, string>();
        var annotations = a.Annotations ?? new Dictionary<string, string>();

        var alertName = labels.GetValueOrDefault("alertname") ?? "Alert";
        var severity = labels.GetValueOrDefault("severity") ?? "info";
        var summary = annotations.GetValueOrDefault("summary") ?? alertName;
        var description = annotations.GetValueOrDefault("description") ?? string.Empty;
        var instance = labels.GetValueOrDefault("instance")
                       ?? labels.GetValueOrDefault("job")
                       ?? string.Empty;
        var status = a.Status ?? "firing";

        var level = severity.ToLowerInvariant() switch
        {
            "critical" => NotificationLevel.Critical,
            "warning" => NotificationLevel.Warning,
            _ => NotificationLevel.Info
        };

        var statusText = status == "resolved" ? "[RESOLVED]" : "[FIRING]";
        var title = $"{statusText} {summary}";

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.Append(description).Append('\n');
        }

        sb.Append($"告警：{alertName} | 级别：{severity}");
        if (!string.IsNullOrWhiteSpace(instance))
        {
            sb.Append($"\n实例：{instance}");
        }

        return (title, sb.ToString(), level);
    }
}
