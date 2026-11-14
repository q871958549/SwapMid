using Volo.Abp.Settings;

namespace SwapMid.Settings;

public class SwapMidSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                SwapMidSettings.AccountDefaultPassword,
                defaultValue: "SwapMid@123456",
                isEncrypted: true),
            new SettingDefinition(
                SwapMidSettings.AccountForceChangePasswordOnFirstLogin,
                defaultValue: "true"),

            new SettingDefinition(
                SwapMidSettings.LayoutNavOrder,
                defaultValue: "[]",
                isVisibleToClients: true),

            new SettingDefinition(
                SwapMidSettings.PreferenceTheme,
                defaultValue: "light",
                isVisibleToClients: true),
            new SettingDefinition(
                SwapMidSettings.PreferenceFontSize,
                defaultValue: "md",
                isVisibleToClients: true),
            new SettingDefinition(
                SwapMidSettings.PreferenceLanguage,
                defaultValue: "zh-Hans",
                isVisibleToClients: true),
            new SettingDefinition(
                SwapMidSettings.PreferenceSidebarCollapsed,
                defaultValue: "false",
                isVisibleToClients: true),

            new SettingDefinition(
                SwapMidSettings.ScheduledJobsPendingCron,
                defaultValue: "{}"),

            new SettingDefinition(
                SwapMidSettings.NotificationAlertWebhookToken,
                defaultValue: "",
                isEncrypted: true),

            new SettingDefinition(SwapMidSettings.NotificationEmailEnabled, defaultValue: "false"),
            new SettingDefinition(SwapMidSettings.NotificationEmailSmtpHost, defaultValue: ""),
            new SettingDefinition(SwapMidSettings.NotificationEmailSmtpPort, defaultValue: "465"),
            new SettingDefinition(SwapMidSettings.NotificationEmailUseSsl, defaultValue: "true"),
            new SettingDefinition(SwapMidSettings.NotificationEmailUserName, defaultValue: ""),
            new SettingDefinition(SwapMidSettings.NotificationEmailPassword, defaultValue: "", isEncrypted: true),
            new SettingDefinition(SwapMidSettings.NotificationEmailFromAddress, defaultValue: ""),
            new SettingDefinition(SwapMidSettings.NotificationEmailFromName, defaultValue: "SwapMid"),

            new SettingDefinition(SwapMidSettings.NotificationWebhookEnabled, defaultValue: "false"),
            new SettingDefinition(SwapMidSettings.NotificationWebhookProvider, defaultValue: "generic"),
            new SettingDefinition(SwapMidSettings.NotificationWebhookUrl, defaultValue: ""),
            new SettingDefinition(SwapMidSettings.NotificationWebhookSecret, defaultValue: "", isEncrypted: true),
            new SettingDefinition(SwapMidSettings.NotificationWebhookMentionUsers, defaultValue: "")
        );
    }
}
