namespace SwapMid.Layout;

/// <summary>当前用户生效的界面偏好（主题 / 字号 / 语言），每账号独立、跨设备随账号同步。</summary>
public class UserPreferenceDto
{
    /// <summary>主题：system（跟随系统）/ light（浅色）/ dark（深色）。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>界面字号（px，13~18）。</summary>
    public int FontSize { get; set; } = 16;

    /// <summary>界面语言：zh-Hans（简体）/ zh-Hant（繁体）/ en（英语）。</summary>
    public string Language { get; set; } = "zh-Hans";
}

/// <summary>保存界面偏好入参（仅传入的字段被更新，null 表示不改该项）。</summary>
public class SaveUserPreferenceInput
{
    /// <summary>主题：system / light / dark。</summary>
    public string? Theme { get; set; }

    /// <summary>界面字号（px，13~18，越界自动收敛）。</summary>
    public int? FontSize { get; set; }

    /// <summary>界面语言：zh-Hans / zh-Hant / en。</summary>
    public string? Language { get; set; }
}
