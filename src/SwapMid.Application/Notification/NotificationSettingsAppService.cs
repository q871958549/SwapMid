using System.Security.Cryptography;
using System.Threading.Tasks;
using SwapMid.Notification.Channels;
using SwapMid.Permissions;
using SwapMid.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.SettingManagement;

namespace SwapMid.Notification;

[Authorize(SwapMidPermissions.NotificationCenter.Settings.Default)]
public class NotificationSettingsAppService : SwapMidAppService, INotificationSettingsAppService
{
    private const string ClearSecretSentinel = "__CLEAR__";

    private readonly ISettingManager _settingManager;
    private readonly INotificationService _notificationService;
    private readonly IConfiguration _configuration;

    public NotificationSettingsAppService(
        ISettingManager settingManager,
        INotificationService notificationService,
        IConfiguration configuration)
    {
        _settingManager = settingManager;
        _notificationService = notificationService;
        _configuration = configuration;
    }

    public async Task<NotificationSettingsDto> GetAsync()
    {
        var webhookEnabled = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookEnabled);
        var webhookProvider = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookProvider);
        var webhookUrl = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookUrl);
        var webhookSecret = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookSecret);
        var webhookMentions = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationWebhookMentionUsers);
        var alertToken = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationAlertWebhookToken);

        var emailEnabled = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailEnabled);
        var smtpHost = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailSmtpHost);
        var smtpPortStr = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailSmtpPort);
        var smtpUseSsl = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailUseSsl);
        var smtpUserName = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailUserName);
        var smtpPassword = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailPassword);
        var fromAddress = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailFromAddress);
        var fromName = await SettingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailFromName);

        return new NotificationSettingsDto
        {
            WebhookEnabled = webhookEnabled == "true",
            WebhookProvider = string.IsNullOrWhiteSpace(webhookProvider) ? "generic" : webhookProvider,
            WebhookUrl = webhookUrl ?? string.Empty,
            WebhookSecretConfigured = !string.IsNullOrWhiteSpace(webhookSecret),
            WebhookMentionUsers = webhookMentions ?? string.Empty,

            EmailEnabled = emailEnabled == "true",
            SmtpHost = smtpHost ?? string.Empty,
            SmtpPort = int.TryParse(smtpPortStr, out var port) ? port : 465,
            SmtpUseSsl = smtpUseSsl != "false",
            SmtpUserName = smtpUserName ?? string.Empty,
            SmtpPasswordConfigured = !string.IsNullOrWhiteSpace(smtpPassword),
            FromAddress = fromAddress ?? string.Empty,
            FromName = fromName ?? "SwapMid",

            AlertTokenConfigured = !string.IsNullOrWhiteSpace(alertToken),
            AlertWebhookPath = "/api/v1/notification/webhooks/alertmanager"
        };
    }

    public async Task UpdateAsync(UpdateNotificationSettingsDto input)
    {
        if (input.WebhookEnabled &&
            !WebhookNotificationChannel.TryValidateWebhookUri(input.WebhookUrl, _configuration, out _))
        {
            throw new UserFriendlyException(
                "启用 Webhook 渠道时，Webhook 地址必须是无用户信息与片段的 HTTPS 地址，且域名属于标准机器人平台或 Notification:Webhook:AllowedHosts 白名单。");
        }

        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationWebhookEnabled, input.WebhookEnabled ? "true" : "false");
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationWebhookProvider, (input.WebhookProvider ?? "generic").Trim().ToLowerInvariant());
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationWebhookUrl, input.WebhookUrl?.Trim() ?? string.Empty);
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationWebhookMentionUsers, input.WebhookMentionUsers?.Trim() ?? string.Empty);

        if (input.WebhookSecret == ClearSecretSentinel)
        {
            await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationWebhookSecret, string.Empty);
        }
        else if (!string.IsNullOrWhiteSpace(input.WebhookSecret))
        {
            await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationWebhookSecret, input.WebhookSecret.Trim());
        }

        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationEmailEnabled, input.EmailEnabled ? "true" : "false");
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationEmailSmtpHost, input.SmtpHost?.Trim() ?? string.Empty);
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationEmailSmtpPort, input.SmtpPort.ToString());
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationEmailUseSsl, input.SmtpUseSsl ? "true" : "false");
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationEmailUserName, input.SmtpUserName?.Trim() ?? string.Empty);
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationEmailFromAddress, input.FromAddress?.Trim() ?? string.Empty);
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationEmailFromName,
            string.IsNullOrWhiteSpace(input.FromName) ? "SwapMid" : input.FromName.Trim());

        if (!string.IsNullOrWhiteSpace(input.SmtpPassword))
        {
            await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationEmailPassword, input.SmtpPassword.Trim());
        }
    }

    public async Task<NotificationTestResultDto> SendTestAsync(SendTestNotificationDto input)
    {
        string? toUser = string.IsNullOrWhiteSpace(input.ToUser) ? null : input.ToUser.Trim();
        if (input.Channel == NotificationChannelType.Email && toUser == null)
        {
            throw new UserFriendlyException("邮件测试必须填写测试收件人邮箱");
        }

        if (input.Channel == NotificationChannelType.Webhook && toUser == null)
        {
            toUser = "@test";
        }

        var result = await _notificationService.SendAsync(new NotificationSendRequest
        {
            Channel = input.Channel,
            Level = input.Level,
            ToUser = toUser,
            Title = "SwapMid 通知渠道连通性测试",
            Content = $"该消息由 SwapMid 通知中心管理页面手动触发。\n级别：{input.Level}",
            Source = NotificationSources.NotificationTest
        });

        return new NotificationTestResultDto { Success = result.Success, Error = result.Error };
    }

    public async Task<string> RegenerateAlertTokenAsync()
    {
        var token = RandomNumberGenerator.GetHexString(32, lowercase: true);
        await _settingManager.SetGlobalAsync(SwapMidSettings.NotificationAlertWebhookToken, token);
        return token;
    }
}
