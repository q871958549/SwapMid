using Shouldly;
using Xunit;

namespace SwapMid.Notification;

/// <summary>通知深链只允许本域前端基址 + 白名单相对路径，且拒绝路径穿越与伪前缀绕过。</summary>
public class NotificationActionLinkTests
{
    [Theory]
    [InlineData("https://app.swapmid.dev", "/data-integration/pipelines", "https://app.swapmid.dev/data-integration/pipelines")]
    [InlineData("https://app.swapmid.dev:8443", "/data-integration/executions?status=failed", "https://app.swapmid.dev:8443/data-integration/executions?status=failed")]
    [InlineData("https://app.swapmid.dev/", "/message-bus/records", "https://app.swapmid.dev/message-bus/records")]
    [InlineData("http://localhost:3000", "/api-management/routes", "http://localhost:3000/api-management/routes")]
    [InlineData("http://127.0.0.1:3000", "/dashboard", "http://127.0.0.1:3000/dashboard")]
    [InlineData("https://app.swapmid.dev", "/system/scheduled-jobs", "https://app.swapmid.dev/system/scheduled-jobs")]
    [InlineData("https://app.swapmid.dev", "/system/system-health", "https://app.swapmid.dev/system/system-health")]
    public void 合法基址与路径_拼出同源绝对链接(string frontend, string path, string expected)
    {
        NotificationActionLink.TryCreate(frontend, path, out var url).ShouldBeTrue();
        url.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "/data-integration/pipelines")]
    [InlineData("", "/data-integration/pipelines")]
    [InlineData("http://evil.example.com", "/data-integration/pipelines")]
    [InlineData("https://app.swapmid.dev", "https://evil.example.com")]
    [InlineData("https://app.swapmid.dev", "//evil.example.com")]
    [InlineData("https://app.swapmid.dev", "/Account/Login")]
    [InlineData("https://app.swapmid.dev", "/untrusted/path")]
    [InlineData("https://app.swapmid.dev", "/data-integration/../Account/Login")]
    [InlineData("https://app.swapmid.dev", "/system/scheduled-jobs#evil")]
    [InlineData("https://app.swapmid.dev", "/data-integration/pipelines https://evil.example.com")]
    [InlineData("https://app.swapmid.dev", "/data-integration/foo://bar")]
    [InlineData("https://app.swapmid.dev", "/data-integration/pipelines\n")]
    public void 非法基址或路径_不加链接(string? frontend, string? path)
    {
        NotificationActionLink.TryCreate(frontend, path, out var url).ShouldBeFalse();
        url.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://app.swapmid.dev:8443", "https://app.swapmid.dev", "https://app.swapmid.dev:8443")]
    [InlineData(null, "https://app.swapmid.dev", "https://app.swapmid.dev")]
    [InlineData("", "http://localhost:3000", "http://localhost:3000")]
    [InlineData("  ", "https://app.swapmid.dev", "https://app.swapmid.dev")]
    public void 通知基址优先于前端基址(string? notification, string? frontend, string expected)
    {
        NotificationActionLink.ResolveFrontendUrl(notification, frontend).ShouldBe(expected);
    }
}
