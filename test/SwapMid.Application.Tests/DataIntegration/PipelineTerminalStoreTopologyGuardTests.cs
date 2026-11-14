using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Monitoring;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class PipelineTerminalStoreTopologyGuardTests
{
    [Fact]
    public async Task 应在同一Host作用域解析Default与后台队列并恢复调用方租户()
    {
        var tenantId = Guid.NewGuid();
        const string connectionString =
            "Host=database.test;Database=swapmid;Username=app;Password=TEST_ONLY";
        var fixture = CreateFixture(
            tenantId,
            connectionString,
            connectionString);

        var pin = await fixture.Guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);
        using var scope = fixture.Guard.EnterSafeScope(
            pin,
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);

        fixture.Calls.Count.ShouldBe(2);
        fixture.Calls[0].Name.ShouldBe(
            ConnectionStrings.DefaultConnectionStringName);
        fixture.Calls[0].TenantId.ShouldBeNull();
        fixture.Calls[1].Name.ShouldBe(
            AbpBackgroundJobsDbProperties.ConnectionStringName);
        fixture.Calls[1].TenantId.ShouldBeNull();
        fixture.CurrentTenant.Id.ShouldBe(tenantId);
        fixture.Guard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.TerminalFinalize);
    }

    [Fact]
    public async Task 目标回执成功核验只固定HostDefault不应被独立后台队列库阻断()
    {
        const string platformConnection =
            "Host=platform.test;Database=swapmid;Username=app;Password=TEST_ONLY";
        var fixture = CreateFixture(
            Guid.NewGuid(),
            platformConnection,
            "Host=jobs.test;Database=jobs;Username=app;Password=TEST_ONLY");

        var pin = await fixture.Guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification);
        using var scope = fixture.Guard.EnterSafeScope(
            pin,
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification);

        fixture.Calls.Count.ShouldBe(1);
        fixture.Calls[0].Name.ShouldBe(
            ConnectionStrings.DefaultConnectionStringName);
        fixture.Calls[0].TenantId.ShouldBeNull();
        fixture.Guard.EnsureSafeScope(
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification);
    }

    [Fact]
    public async Task ConfirmedApplied只固定HostDefault且不得被独立后台队列库阻断()
    {
        var tenantId = Guid.NewGuid();
        const string platformConnection =
            "Host=platform.test;Database=swapmid;Username=app;Password=TEST_ONLY";
        var fixture = CreateFixture(
            tenantId,
            platformConnection,
            "Host=jobs.test;Database=jobs;Username=app;Password=TEST_ONLY");

        var pin = await fixture.Guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.OutcomeResolutionApplied);
        using (fixture.Guard.EnterSafeScope(
                   pin,
                   PipelineTerminalStoreTopologyPhase
                       .OutcomeResolutionApplied))
        {
            fixture.Calls.Count.ShouldBe(1);
            fixture.Calls[0].Name.ShouldBe(
                ConnectionStrings.DefaultConnectionStringName);
            fixture.Calls[0].TenantId.ShouldBeNull();
            fixture.CurrentTenant.Id.ShouldBe(tenantId,
                "拓扑探测退出时必须恢复 caller tenant");
            fixture.Guard.EnsureSafeScope(
                PipelineTerminalStoreTopologyPhase
                    .OutcomeResolutionApplied);

            var exception = Should.Throw<BusinessException>(() =>
                fixture.Guard.EnsureSafeScope(
                    PipelineTerminalStoreTopologyPhase
                        .NotificationSchedule));
            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionCoordinationUnavailable);
            Should.Throw<BusinessException>(() =>
                fixture.ScopeAccessor.TryResolve(
                    AbpBackgroundJobsDbProperties.ConnectionStringName,
                    out _));
        }

        fixture.CurrentTenant.Id.ShouldBe(tenantId);
    }

    [Fact]
    public async Task ConfirmedNotApplied仍要求Default与后台队列使用同一原始连接串()
    {
        var fixture = CreateFixture(
            Guid.NewGuid(),
            "Host=platform.test;Database=swapmid;Password=TEST_ONLY",
            "Host=jobs.test;Database=jobs;Password=TEST_ONLY");

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            fixture.Guard.CaptureSafeAsync(
                PipelineTerminalStoreTopologyPhase.OutcomeResolution));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        fixture.Calls.Select(call => call.Name).ShouldBe(new[]
        {
            ConnectionStrings.DefaultConnectionStringName,
            AbpBackgroundJobsDbProperties.ConnectionStringName,
        });
        fixture.Calls.ShouldAllBe(call => call.TenantId == null);
    }

    [Fact]
    public async Task 目标回执激活只固定HostDefault且退出后恢复调用方作用域()
    {
        var tenantId = Guid.NewGuid();
        const string platformConnection =
            "Host=platform.test;Database=swapmid;Username=app;Password=TEST_ONLY";
        var fixture = CreateFixture(
            tenantId,
            platformConnection,
            "Host=jobs.test;Database=jobs;Username=app;Password=TEST_ONLY");

        var pin = await fixture.Guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.TargetReceiptActivation);
        using (fixture.Guard.EnterSafeScope(
                   pin,
                   PipelineTerminalStoreTopologyPhase
                       .TargetReceiptActivation))
        {
            fixture.Calls.Count.ShouldBe(1);
            fixture.Calls[0].Name.ShouldBe(
                ConnectionStrings.DefaultConnectionStringName);
            fixture.Calls[0].TenantId.ShouldBeNull();
            fixture.CurrentTenant.Id.ShouldBe(tenantId);
            fixture.Guard.EnsureSafeScope(
                PipelineTerminalStoreTopologyPhase
                    .TargetReceiptActivation);
            fixture.ScopeAccessor.TryResolve(
                    ConnectionStrings.DefaultConnectionStringName,
                    out var pinned)
                .ShouldBeTrue();
            pinned.ShouldBe(platformConnection);
        }

        fixture.CurrentTenant.Id.ShouldBe(tenantId);
        fixture.ScopeAccessor.TryResolve(
                ConnectionStrings.DefaultConnectionStringName,
                out _)
            .ShouldBeFalse("activation 调用退出后不得遗留 pinned scope");
    }

    [Fact]
    public async Task 激活Pin应隔离探针期间的Default配置热变()
    {
        const string capturedConnection =
            "Host=before.test;Database=swapmid;Username=app;Password=TEST_ONLY";
        const string changedConnection =
            "Host=after.test;Database=swapmid;Username=app;Password=TEST_ONLY";
        var configuredConnection = capturedConnection;
        var fixture = CreateFixture(
            Guid.NewGuid(),
            capturedConnection,
            "unused-jobs",
            platformConnectionProvider: () => configuredConnection);

        var pin = await fixture.Guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.TargetReceiptActivation);
        using (fixture.Guard.EnterSafeScope(
                   pin,
                   PipelineTerminalStoreTopologyPhase
                       .TargetReceiptActivation))
        {
            configuredConnection = changedConnection;

            fixture.ScopeAccessor.TryResolve(
                    ConnectionStrings.DefaultConnectionStringName,
                    out var pinned)
                .ShouldBeTrue();
            pinned.ShouldBe(capturedConnection,
                "外部探针期间配置热变后，第二段 UoW 必须继续解析到捕获值");
            fixture.Guard.EnsureSafeScope(
                PipelineTerminalStoreTopologyPhase
                    .TargetReceiptActivation);
            fixture.Calls.Count.ShouldBe(1,
                "进入 pin 后不得重新动态解析 Default");
        }

        fixture.ScopeAccessor.TryResolve(
                ConnectionStrings.DefaultConnectionStringName,
                out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task 同库但原始连接串文本不同时应失败关闭且不得泄露凭据()
    {
        const string secret = "P1_TOPOLOGY_SECRET";
        var fixture = CreateFixture(
            Guid.NewGuid(),
            $"Host=database.test;Database=swapmid;Password={secret}",
            $"Database=swapmid;Host=database.test;Password={secret}");
        var counter = PlatformMetrics.EtlExecutionCoordinationEventsTotal
            .WithLabels(PipelineTerminalStoreTopologyGuard.MismatchOutcome);
        var counterBefore = counter.Value;

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            fixture.Guard.CaptureSafeAsync(
                PipelineTerminalStoreTopologyPhase.TerminalFinalize));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        exception.InnerException.ShouldBeNull();
        exception.ToString().ShouldNotContain(secret);
        fixture.Logger.Messages.ShouldAllBe(message =>
            !message.Contains(secret, StringComparison.Ordinal));
        fixture.Logger.Exceptions.ShouldAllBe(value => value == null);
        fixture.CurrentTenant.Id.ShouldBe(fixture.TenantId);
        fixture.Logger.Levels.ShouldContain(LogLevel.Critical);
        fixture.Logger.Messages.ShouldContain(message =>
            message.Contains(
                PipelineTerminalStoreTopologyGuard.MismatchOutcome,
                StringComparison.Ordinal));
        counter.Value.ShouldBeGreaterThanOrEqualTo(counterBefore + 1);
    }

    [Fact]
    public async Task 连接解析异常应转为稳定低敏错误且恢复租户作用域()
    {
        const string secret = "Password=P1_RESOLVER_SECRET";
        var fixture = CreateFixture(
            Guid.NewGuid(),
            "unused",
            "unused",
            new InvalidOperationException(secret));
        var counter = PlatformMetrics.EtlExecutionCoordinationEventsTotal
            .WithLabels(
                PipelineTerminalStoreTopologyGuard.ProbeFailureOutcome);
        var counterBefore = counter.Value;

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            fixture.Guard.CaptureSafeAsync(
                PipelineTerminalStoreTopologyPhase.NotificationSchedule));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        exception.InnerException.ShouldBeNull();
        exception.ToString().ShouldNotContain(secret);
        fixture.Logger.Messages.ShouldAllBe(message =>
            !message.Contains(secret, StringComparison.Ordinal));
        fixture.Logger.Messages.ShouldContain(message =>
            message.Contains(
                typeof(InvalidOperationException).FullName!,
                StringComparison.Ordinal));
        fixture.Logger.Exceptions.ShouldAllBe(value => value == null);
        fixture.CurrentTenant.Id.ShouldBe(fixture.TenantId);
        counter.Value.ShouldBeGreaterThanOrEqualTo(counterBefore + 1);
    }

    [Fact]
    public async Task 调用方取消应原样传播且不得探测或记录拓扑故障()
    {
        var fixture = CreateFixture(
            Guid.NewGuid(),
            "tenant-connection",
            "host-connection");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.Guard.CaptureSafeAsync(
                PipelineTerminalStoreTopologyPhase.ExecutionAdmission,
                cancellation.Token));

        fixture.Calls.ShouldBeEmpty();
        fixture.Logger.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void 已验证连接在作用域内应固定Default与后台队列且退出后清空()
    {
        const string connectionString =
            "Host=pinned.test;Database=swapmid;Password=TEST_ONLY";
        var accessor = new PipelineTerminalStoreConnectionScopeAccessor();

        using (accessor.Enter(connectionString))
        {
            accessor.TryResolve(
                    ConnectionStrings.DefaultConnectionStringName,
                    out var platform)
                .ShouldBeTrue();
            platform.ShouldBe(connectionString);
            accessor.TryResolve(
                    AbpBackgroundJobsDbProperties.ConnectionStringName,
                    out var jobs)
                .ShouldBeTrue();
            jobs.ShouldBe(connectionString);
            accessor.TryResolve("Unrelated", out _).ShouldBeFalse();
        }

        accessor.TryResolve(
                ConnectionStrings.DefaultConnectionStringName,
                out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task 父作用域退出后继承Holder的延迟任务必须失败关闭而非回退动态配置()
    {
        const string connectionString =
            "Host=pinned.test;Database=swapmid;Password=TEST_ONLY";
        var accessor = new PipelineTerminalStoreConnectionScopeAccessor();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task delayed;

        using (accessor.Enter(connectionString))
        {
            delayed = Task.Run(async () =>
            {
                started.SetResult();
                await release.Task;
                accessor.TryResolve("Unrelated", out _).ShouldBeFalse();
                accessor.TryResolve(
                    ConnectionStrings.DefaultConnectionStringName,
                    out _);
            });
            await started.Task;
        }

        release.SetResult();
        var exception = await Should.ThrowAsync<BusinessException>(async () =>
            await delayed);
        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
    }

    [Fact]
    public async Task 已有Ambient事务时不得事后捕获并覆盖连接拓扑()
    {
        var fixture = CreateFixture(
            Guid.NewGuid(),
            "host-platform",
            "host-jobs");
        fixture.UnitOfWorkManager.Current.Returns(
            Substitute.For<IUnitOfWork>());

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            fixture.Guard.CaptureSafeAsync(
                PipelineTerminalStoreTopologyPhase.TerminalFinalize));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
        fixture.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task 同一Pin只能进入一次避免过期拓扑被重新启用()
    {
        var fixture = CreateFixture(
            Guid.NewGuid(),
            "host-platform",
            "host-platform");
        var pin = await fixture.Guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);

        using (fixture.Guard.EnterSafeScope(
                   pin,
                   PipelineTerminalStoreTopologyPhase.ExecutionAdmission))
        {
        }

        var exception = Should.Throw<BusinessException>(() =>
            fixture.Guard.EnterSafeScope(
                pin,
                PipelineTerminalStoreTopologyPhase.ExecutionAdmission));
        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
    }

    [Fact]
    public async Task 继承已撤销Holder的延迟任务不得用另一Pin重新进入()
    {
        var fixture = CreateFixture(
            Guid.NewGuid(),
            "host-platform",
            "host-platform");
        var activePin = await fixture.Guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);
        var unusedPin = await fixture.Guard.CaptureSafeAsync(
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task delayed;

        using (fixture.Guard.EnterSafeScope(
                   activePin,
                   PipelineTerminalStoreTopologyPhase.ExecutionAdmission))
        {
            delayed = Task.Run(async () =>
            {
                started.SetResult();
                await release.Task;
                using var ignored = fixture.Guard.EnterSafeScope(
                    unusedPin,
                    PipelineTerminalStoreTopologyPhase.ExecutionAdmission);
            });
            await started.Task;
        }

        release.SetResult();
        var exception = await Should.ThrowAsync<BusinessException>(async () =>
            await delayed);
        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
    }

    private static Fixture CreateFixture(
        Guid tenantId,
        string tenantConnectionString,
        string hostJobsConnectionString,
        Exception? resolverException = null,
        Func<string>? platformConnectionProvider = null)
    {
        Guid? currentTenantId = tenantId;
        string? currentTenantName = "tenant-test";
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns(_ => currentTenantId);
        currentTenant.Name.Returns(_ => currentTenantName);
        currentTenant.IsAvailable.Returns(_ => currentTenantId.HasValue);
        currentTenant.Change(
                Arg.Any<Guid?>(),
                Arg.Any<string?>())
            .Returns(call =>
            {
                var previousId = currentTenantId;
                var previousName = currentTenantName;
                currentTenantId = call.ArgAt<Guid?>(0);
                currentTenantName = call.ArgAt<string?>(1);
                return new RestoreScope(() =>
                {
                    currentTenantId = previousId;
                    currentTenantName = previousName;
                });
            });

        var calls = new List<ResolutionCall>();
        var resolver = Substitute.For<IConnectionStringResolver>();
        resolver.ResolveAsync(Arg.Any<string>()).Returns(call =>
        {
            var name = call.Arg<string>();
            calls.Add(new ResolutionCall(name, currentTenantId));
            if (resolverException != null &&
                name == AbpBackgroundJobsDbProperties.ConnectionStringName)
            {
                throw resolverException;
            }

            var value = name ==
                        ConnectionStrings.DefaultConnectionStringName
                ? platformConnectionProvider?.Invoke() ??
                  tenantConnectionString
                : hostJobsConnectionString;
            return Task.FromResult(value);
        });
        var logger = new RecordingLogger<
            PipelineTerminalStoreTopologyGuard>();
        var scopeAccessor =
            new PipelineTerminalStoreConnectionScopeAccessor();
        var unitOfWorkManager = Substitute.For<IUnitOfWorkManager>();
        unitOfWorkManager.Current.Returns((IUnitOfWork?)null);
        var guard = new PipelineTerminalStoreTopologyGuard(
            resolver,
            currentTenant,
            scopeAccessor,
            unitOfWorkManager,
            logger);
        return new Fixture(
            guard,
            currentTenant,
            tenantId,
            unitOfWorkManager,
            calls,
            logger,
            scopeAccessor);
    }

    private sealed record ResolutionCall(string Name, Guid? TenantId);

    private sealed record Fixture(
        PipelineTerminalStoreTopologyGuard Guard,
        ICurrentTenant CurrentTenant,
        Guid TenantId,
        IUnitOfWorkManager UnitOfWorkManager,
        List<ResolutionCall> Calls,
        RecordingLogger<PipelineTerminalStoreTopologyGuard> Logger,
        PipelineTerminalStoreConnectionScopeAccessor ScopeAccessor);

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose()
        {
            var action = _restore;
            _restore = null;
            action?.Invoke();
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public List<LogLevel> Levels { get; } = [];

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
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
