using System.Collections.Generic;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SwapMid.Xunit;

/// <summary>
/// <see cref="RetryFactAttribute"/> 的测试用例发现器:把标注方法包装成 <see cref="RetryTestCase"/>。
/// 结构照抄 xunit v2 官方示例仓 samples.xunit 的 RetryFactExample。
/// </summary>
public class RetryFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diagnosticMessageSink;

    public RetryFactDiscoverer(IMessageSink diagnosticMessageSink)
    {
        _diagnosticMessageSink = diagnosticMessageSink;
    }

    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
    {
        var maxRetries = factAttribute.GetNamedArgument<int>(nameof(RetryFactAttribute.MaxRetries));
        if (maxRetries < 1)
        {
            maxRetries = 3;
        }

        yield return new RetryTestCase(
            _diagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod,
            maxRetries);
    }
}
