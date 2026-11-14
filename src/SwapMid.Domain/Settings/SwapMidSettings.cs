namespace SwapMid.Settings;

public static class SwapMidSettings
{
    private const string Prefix = "SwapMid";

    // ==================== 账号安全策略 ====================
    public const string AccountDefaultPassword = Prefix + ".Account.DefaultPassword";
    public const string AccountForceChangePasswordOnFirstLogin = Prefix + ".Account.ForceChangePasswordOnFirstLogin";

    // ==================== 导航栏个人偏好（User 级） ====================
    public const string LayoutNavOrder = Prefix + ".Layout.NavOrder";

    // ==================== 外观与语言个人偏好（User 级） ====================
    public const string PreferenceTheme = Prefix + ".Preference.Theme";
    public const string PreferenceFontSize = Prefix + ".Preference.FontSize";
    public const string PreferenceLanguage = Prefix + ".Preference.Language";
    public const string PreferenceSidebarCollapsed = Prefix + ".Preference.SidebarCollapsed";

    // ==================== 定时任务 Cron 待同步队列 ====================
    public const string ScheduledJobsPendingCron = Prefix + ".ScheduledJobs.PendingCron";

    // ==================== 通用通知中心配置 ====================
    public const string NotificationAlertWebhookToken = Prefix + ".Notification.AlertWebhookToken";

    public const string NotificationEmailEnabled = Prefix + ".Notification.Email.Enabled";
    public const string NotificationEmailSmtpHost = Prefix + ".Notification.Email.SmtpHost";
    public const string NotificationEmailSmtpPort = Prefix + ".Notification.Email.SmtpPort";
    public const string NotificationEmailUseSsl = Prefix + ".Notification.Email.UseSsl";
    public const string NotificationEmailUserName = Prefix + ".Notification.Email.UserName";
    public const string NotificationEmailPassword = Prefix + ".Notification.Email.Password";
    public const string NotificationEmailFromAddress = Prefix + ".Notification.Email.FromAddress";
    public const string NotificationEmailFromName = Prefix + ".Notification.Email.FromName";

    public const string NotificationWebhookEnabled = Prefix + ".Notification.Webhook.Enabled";
    public const string NotificationWebhookProvider = Prefix + ".Notification.Webhook.Provider";
    public const string NotificationWebhookUrl = Prefix + ".Notification.Webhook.Url";
    public const string NotificationWebhookSecret = Prefix + ".Notification.Webhook.Secret";
    public const string NotificationWebhookMentionUsers = Prefix + ".Notification.Webhook.MentionUsers";
}
