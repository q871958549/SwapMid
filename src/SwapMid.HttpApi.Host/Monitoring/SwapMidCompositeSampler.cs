using OpenTelemetry.Trace;

namespace SwapMid.Monitoring;

/// <summary>
/// D5 复合采样器（ADR-0007 P3.0）：关键链路强采 100% + 其余 head 抽样。
/// 带 <see cref="SwapMidTracing.CriticalTag"/>=true 初始 tag 的 span（ETL 关键路径、消息投递、Hangfire 作业根）
/// 一律 <see cref="SamplingDecision.RecordAndSample"/>，不受 parent 抽样决定影响——确保慢/失败的关键链路
/// 不被 10% 抽样丢弃；其余 span 委托 <see cref="ParentBasedSampler"/>(<see cref="TraceIdRatioBasedSampler"/>(ratio))。
/// 注：OTel .NET 的 <c>SamplingParameters</c> 不暴露 ActivitySource（无 Source 字段），故按 span 的初始 tag 判定，
/// 而非 ADR 原设想的 source 名（P3.0 落地订正）；强采标记由 <see cref="SwapMidTracing.StartCritical"/> 注入。
/// 本类放 Host 而非 Application：依赖 OTel SDK 的 <see cref="Sampler"/>，不应把 SDK 依赖下沉到 Application 层。
/// </summary>
public sealed class SwapMidCompositeSampler : Sampler
{
    private static readonly SamplingResult RecordAndSampleResult = new(SamplingDecision.RecordAndSample);
    private readonly Sampler _fallback;

    public SwapMidCompositeSampler(double ratio)
    {
        _fallback = new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio));
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        var tags = samplingParameters.Tags;
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == SwapMidTracing.CriticalTag && tag.Value is true)
                {
                    return RecordAndSampleResult;
                }
            }
        }

        return _fallback.ShouldSample(samplingParameters);
    }
}
