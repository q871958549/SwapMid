using System.Collections.Generic;
using System.Diagnostics;

namespace SwapMid.Monitoring;

/// <summary>
/// 平台分布式追踪自定义埋点入口（OpenTelemetry）。
/// 与 <see cref="PlatformMetrics"/> 正交并存：metrics 走 prometheus-net，traces 走本 <see cref="ActivitySource"/>。
/// </summary>
public static class SwapMidTracing
{
    /// <summary>ActivitySource 名称，须与 Host 端 <c>AddSource(...)</c> 一致。</summary>
    public const string ActivitySourceName = "SwapMid";

    /// <summary>
    /// 「关键链路」标记 tag。带此初始 tag（值 <c>true</c>）的 span 会被 <c>SwapMidCompositeSampler</c> 强采 100%，
    /// 确保数据同步、消息投递、Hangfire 后台作业等关键链路不被 head 采样丢弃。
    /// </summary>
    public const string CriticalTag = "swapmid.critical";

    /// <summary>全平台共享的业务 <see cref="ActivitySource"/>。</summary>
    public static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>
    /// 起一个「关键链路」span，带 <see cref="CriticalTag"/>=true 初始 tag（强采 100%）。
    /// 未注册 listener 时返回 <c>null</c>，调用方使用 <c>activity?.</c> 即零开销。
    /// </summary>
    public static Activity? StartCritical(string name, ActivityKind kind = ActivityKind.Internal)
    {
        var parentContext = Activity.Current?.Context ?? default;
        return Source.StartActivity(
            name,
            kind,
            parentContext,
            tags: new[] { new KeyValuePair<string, object?>(CriticalTag, true) });
    }
}
