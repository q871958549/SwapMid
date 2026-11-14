using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;

namespace SwapMid.Notification.Channels;

/// <summary>
/// 邮件渠道：经 SMTP（MailKit）把通用通知发成邮件。SMTP 参数读 ABP Setting（前端可配，密码加密）。
/// 收件人 <see cref="NotificationSendRequest.ToUser"/> 为邮箱地址，多个用 , ; | 分隔（由通知中心按接收人聚合）。
/// </summary>
[ExposeServices(typeof(INotificationChannel))]
public class EmailNotificationChannel : INotificationChannel, ITransientDependency
{
    private readonly ISettingProvider _settingProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailNotificationChannel> _logger;

    public EmailNotificationChannel(
        ISettingProvider settingProvider,
        IConfiguration configuration,
        ILogger<EmailNotificationChannel> logger)
    {
        _settingProvider = settingProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public NotificationChannelType ChannelType => NotificationChannelType.Email;

    public async Task<bool> IsEnabledAsync()
    {
        if (await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailEnabled) != "true")
            return false;
        var host = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailSmtpHost);
        var from = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailFromAddress);
        return !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(from);
    }

    public async Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var host = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailSmtpHost);
            var portStr = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailSmtpPort);
            var useSsl = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailUseSsl) == "true";
            var from = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailFromAddress);
            var fromName = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailFromName);
            var userName = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailUserName);
            var password = await _settingProvider.GetOrNullAsync(SwapMidSettings.NotificationEmailPassword);

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
                return NotificationSendResult.Fail("邮件渠道未配置（SMTP 主机 / 发件人）");

            var recipients = (request.ToUser ?? string.Empty)
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .ToList();
            if (recipients.Count == 0)
                return NotificationSendResult.Fail("无邮件接收人");

            var port = int.TryParse(portStr, out var p) ? p : 465;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(fromName) ? from : fromName, from));
            foreach (var to in recipients)
            {
                try { message.To.Add(MailboxAddress.Parse(to)); }
                catch { /* 跳过非法地址，不因一个坏地址整封失败 */ }
            }
            if (message.To.Count == 0)
                return NotificationSendResult.Fail("无有效邮件接收人地址");

            message.Subject = request.Title;
            message.Body = BuildBody(request, _configuration["App:FrontendUrl"]);

            using var client = new SmtpClient();
            // 465=隐式 SSL；其余走 STARTTLS（可用则升级，否则明文，兼容内网无 TLS 的网关）
            var socketOptions = useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(host, port, socketOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(userName))
                await client.AuthenticateAsync(userName, password ?? string.Empty, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return NotificationSendResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "邮件通知发送失败 title={Title}", request.Title);
            return NotificationSendResult.Fail(ex.Message);
        }
    }

    internal static MimeEntity BuildBody(NotificationSendRequest r, string? frontendUrl = null)
    {
        var levelText = r.Level switch
        {
            NotificationLevel.Critical => "🔴 严重",
            NotificationLevel.Warning => "🟠 警告",
            _ => "🟢 提示"
        };
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Segoe UI,Microsoft YaHei,sans-serif;font-size:14px;line-height:1.6;color:#222\">");
        sb.Append($"<h3 style=\"margin:0 0 8px\">{System.Net.WebUtility.HtmlEncode(r.Title)}</h3>");
        sb.Append($"<p style=\"margin:0 0 8px;color:#888\">级别：{levelText}");
        if (!string.IsNullOrWhiteSpace(r.Source))
            sb.Append($"　来源：{System.Net.WebUtility.HtmlEncode(r.Source)}");
        sb.Append("</p>");
        if (!string.IsNullOrWhiteSpace(r.Content))
            sb.Append($"<pre style=\"white-space:pre-wrap;font-family:inherit;margin:0\">{System.Net.WebUtility.HtmlEncode(r.Content)}</pre>");
        if (NotificationActionLink.TryCreate(frontendUrl, r.ActionPath, out var actionUrl))
        {
            var label = System.Net.WebUtility.HtmlEncode(
                string.IsNullOrWhiteSpace(r.ActionLabel) ? NotificationActionLink.DefaultLabel : r.ActionLabel);
            sb.Append($"<p style=\"margin:12px 0 0\"><a href=\"{System.Net.WebUtility.HtmlEncode(actionUrl)}\">{label}</a></p>");
        }
        sb.Append("</div>");

        var builder = new BodyBuilder { HtmlBody = sb.ToString() };
        foreach (var att in r.Attachments)
        {
            if (att.Content is { Length: > 0 } && !string.IsNullOrWhiteSpace(att.FileName))
                builder.Attachments.Add(att.FileName, att.Content, ContentType.Parse(att.ContentType));
        }
        return builder.ToMessageBody();
    }
}
