using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SwapMid.Xunit;

/// <summary>
/// 失败自动重跑的测试用例(配合 <see cref="RetryFactAttribute"/>):每轮把结果消息缓冲进
/// <see cref="DelayedMessageBus"/>,失败且还有余量就丢弃本轮消息重跑(xunit 会重建测试类实例——
/// 对 AbpIntegratedTest 派生类即全新 ABP Application/SQLite 连接),否则把最后一轮消息转发给真总线。
/// 结构照抄 xunit v2 官方示例仓 samples.xunit 的 RetryFactExample。
/// </summary>
public class RetryTestCase : XunitTestCase
{
    private int _maxRetries;

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes", true)]
    public RetryTestCase()
    {
    }

    public RetryTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        int maxRetries)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments: null)
    {
        _maxRetries = maxRetries;
    }

    public override async Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var runCount = 0;

        while (true)
        {
            // 失败轮次的消息(含失败断言)整体丢弃,外界只看到最后一轮;成功轮/末轮才转发。
            var delayedMessageBus = new DelayedMessageBus(messageBus);

            var summary = await base.RunAsync(
                diagnosticMessageSink, delayedMessageBus, constructorArguments, aggregator, cancellationTokenSource);
            if (aggregator.HasExceptions || summary.Failed == 0 || ++runCount >= _maxRetries)
            {
                delayedMessageBus.Dispose(); // 转发缓冲消息
                return summary;
            }

            diagnosticMessageSink.OnMessage(new DiagnosticMessage(
                "第 {1} 次执行 \"{0}\" 失败,自动重试(至多 {2} 次)……", DisplayName, runCount, _maxRetries));
        }
    }

    public override void Serialize(IXunitSerializationInfo data)
    {
        base.Serialize(data);
        data.AddValue("MaxRetries", _maxRetries);
    }

    public override void Deserialize(IXunitSerializationInfo data)
    {
        base.Deserialize(data);
        _maxRetries = data.GetValue<int>("MaxRetries");
    }
}
