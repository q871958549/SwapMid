using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class NpgsqlDataPipelineExecutionFenceTests
{
    [Fact]
    public async Task 目标资源集合必须在创建物理会话前执行有界校验()
    {
        var connectionFactory =
            Substitute.For<IDataPipelineExecutionFenceConnectionFactory>();
        var fence = new NpgsqlDataPipelineExecutionFence(
            connectionFactory,
            NullLogger<NpgsqlDataPipelineExecutionFence>.Instance);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            fence.TryAcquireAsync(
                Guid.NewGuid(),
                Array.Empty<string>(),
                capacitySlots: 0));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            fence.TryAcquireAsync(
                Guid.NewGuid(),
                new[] { "legacy", "verified", "unexpected" },
                capacitySlots: 0));
        await Should.ThrowAsync<ArgumentException>(() =>
            fence.TryAcquireAsync(
                Guid.NewGuid(),
                new[] { "legacy", " " },
                capacitySlots: 0));
        await Should.ThrowAsync<ArgumentNullException>(() =>
            fence.TryAcquireAsync(
                Guid.NewGuid(),
                null!,
                capacitySlots: 0));

        await connectionFactory.DidNotReceive().CreateAsync();
    }

    [Fact]
    public void 心跳失败时应先发出失联信号且日志异常不得逸出()
    {
        using var lost = new CancellationTokenSource();
        var logger = new ThrowingLogger(lost.Token);

        Should.NotThrow(() =>
            NpgsqlDataPipelineExecutionFence.SignalHandleLostNoThrow(
                lost,
                logger,
                new InvalidOperationException("heartbeat failed")));

        lost.IsCancellationRequested.ShouldBeTrue();
        logger.WasCalled.ShouldBeTrue();
        logger.TokenWasCancelledWhenCalled.ShouldBeTrue(
            "失联状态转换必须先于任何同步日志提供方调用");
    }

    private sealed class ThrowingLogger
        : ILogger<NpgsqlDataPipelineExecutionFence>
    {
        private readonly CancellationToken _lostToken;

        public ThrowingLogger(CancellationToken lostToken)
        {
            _lostToken = lostToken;
        }

        public bool WasCalled { get; private set; }
        public bool TokenWasCancelledWhenCalled { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            WasCalled = true;
            TokenWasCancelledWhenCalled = _lostToken.IsCancellationRequested;
            throw new InvalidOperationException("logger failed");
        }
    }
}
