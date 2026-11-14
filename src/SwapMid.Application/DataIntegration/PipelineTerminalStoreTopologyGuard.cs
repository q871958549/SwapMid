using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Monitoring;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// 数据集成实体不实现 IMultiTenant，ABP 仓储会在 host scope 使用 Default；需要
/// 后台通知的终态还必须与 BackgroundJobs 落在 ABP 能复用的同一原始连接串上。
/// ABP 10.5 直接用解析后的原始连接串构造 UoW transaction key；即使物理指向同库，
/// 文本不同也不能宣称原子。phase 同时声明 scope 所需能力，host-only pin 不能用于通知。
/// </summary>
public interface IPipelineTerminalStoreTopologyGuard
{
    Task<IPipelineTerminalStoreTopologyPin> CaptureSafeAsync(
        PipelineTerminalStoreTopologyPhase phase,
        CancellationToken cancellationToken = default);

    IDisposable EnterSafeScope(
        IPipelineTerminalStoreTopologyPin pin,
        PipelineTerminalStoreTopologyPhase phase);

    void EnsureSafeScope(PipelineTerminalStoreTopologyPhase phase);
}

/// <summary>
/// 不可读、不可序列化的进程内拓扑凭据。连接串只由生产 guard 私有实现持有。
/// </summary>
public interface IPipelineTerminalStoreTopologyPin
{
}

public enum PipelineTerminalStoreTopologyPhase
{
    ExecutionAdmission = 0,
    Claim = 1,
    TerminalFinalize = 2,
    NotificationSchedule = 3,
    OutcomeResolution = 4,
    RunningRecovery = 5,
    TargetReceiptVerification = 6,
    TargetReceiptActivation = 7,
    OutcomeResolutionApplied = 8,
}

public sealed class PipelineTerminalStoreTopologyGuard :
    IPipelineTerminalStoreTopologyGuard,
    ITransientDependency
{
    public const string MismatchOutcome =
        "terminal_store_topology_mismatch";
    public const string ProbeFailureOutcome =
        "terminal_store_topology_probe_failure";

    private readonly IConnectionStringResolver _connectionStringResolver;
    private readonly ICurrentTenant _currentTenant;
    private readonly PipelineTerminalStoreConnectionScopeAccessor
        _scopeAccessor;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<PipelineTerminalStoreTopologyGuard> _logger;

    public PipelineTerminalStoreTopologyGuard(
        IConnectionStringResolver connectionStringResolver,
        ICurrentTenant currentTenant,
        PipelineTerminalStoreConnectionScopeAccessor scopeAccessor,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<PipelineTerminalStoreTopologyGuard> logger)
    {
        _connectionStringResolver = connectionStringResolver;
        _currentTenant = currentTenant;
        _scopeAccessor = scopeAccessor;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public async Task<IPipelineTerminalStoreTopologyPin> CaptureSafeAsync(
        PipelineTerminalStoreTopologyPhase phase,
        CancellationToken cancellationToken = default)
    {
        var phaseName = GetPhaseName(phase);
        var requiredCapabilities = GetRequiredCapabilities(phase);
        var tenantScoped = _currentTenant.IsAvailable;
        var requiresSharedBackgroundJobs =
            (requiredCapabilities &
             PipelineTerminalStoreTopologyCapability.BackgroundJobs) != 0;
        if (_scopeAccessor.HasScopeHolder ||
            _unitOfWorkManager.Current != null)
        {
            Reject(
                phaseName,
                ProbeFailureOutcome,
                tenantScoped,
                exceptionType: null);
        }

        string? platformConnectionString = null;
        string? backgroundJobsConnectionString = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // DataSource/DataPipeline/PipelineExecution 均非 IMultiTenant，ABP 仓储与
            // BackgroundJobsDbContext 都会在 host scope 解析连接。显式一次性解析并
            // 固定两者，避免 fence 走 caller tenant、实体却走 host 的错位。
            using (_currentTenant.Change(null))
            {
                platformConnectionString =
                    await _connectionStringResolver.ResolveAsync(
                        ConnectionStrings.DefaultConnectionStringName);
                cancellationToken.ThrowIfCancellationRequested();
                if (requiresSharedBackgroundJobs)
                {
                    backgroundJobsConnectionString =
                        await _connectionStringResolver.ResolveAsync(
                            AbpBackgroundJobsDbProperties.ConnectionStringName);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Reject(
                phaseName,
                ProbeFailureOutcome,
                tenantScoped,
                exception.GetType());
        }

        if (string.IsNullOrWhiteSpace(platformConnectionString) ||
            (requiresSharedBackgroundJobs &&
             (string.IsNullOrWhiteSpace(backgroundJobsConnectionString) ||
              !string.Equals(
                  platformConnectionString,
                  backgroundJobsConnectionString,
                  StringComparison.Ordinal))))
        {
            Reject(
                phaseName,
                MismatchOutcome,
                tenantScoped,
                exceptionType: null);
        }

        return new ConnectionPin(
            platformConnectionString,
            requiredCapabilities);
    }

    public IDisposable EnterSafeScope(
        IPipelineTerminalStoreTopologyPin pin,
        PipelineTerminalStoreTopologyPhase phase)
    {
        if (pin is not ConnectionPin connectionPin ||
            _scopeAccessor.HasScopeHolder ||
            _unitOfWorkManager.Current != null)
        {
            Reject(
                GetPhaseName(phase),
                ProbeFailureOutcome,
                _currentTenant.IsAvailable,
                exceptionType: null);
            return null!;
        }

        var requiredCapabilities = GetRequiredCapabilities(phase);
        if (!connectionPin.TryConsume(
                out var connectionString,
                out var capturedCapabilities) ||
            (capturedCapabilities & requiredCapabilities) !=
            requiredCapabilities)
        {
            Reject(
                GetPhaseName(phase),
                ProbeFailureOutcome,
                _currentTenant.IsAvailable,
                exceptionType: null);
        }

        // 必须由 await CaptureSafeAsync 的调用方同步进入；AsyncLocal 在被 await 的
        // 子方法内赋值不会反向传播到调用方 ExecutionContext。
        return _scopeAccessor.Enter(
            connectionString,
            capturedCapabilities);
    }

    public void EnsureSafeScope(PipelineTerminalStoreTopologyPhase phase)
    {
        if (_scopeAccessor.Supports(GetRequiredCapabilities(phase)))
        {
            return;
        }

        Reject(
            GetPhaseName(phase),
            ProbeFailureOutcome,
            _currentTenant.IsAvailable,
            exceptionType: null);
    }

    [DoesNotReturn]
    private void Reject(
        string phase,
        string reason,
        bool tenantScoped,
        Type? exceptionType)
    {
        PlatformMetrics.EtlExecutionCoordinationEventsTotal
            .WithLabels(reason)
            .Inc();

        if (exceptionType == null)
        {
            _logger.LogCritical(
                "ETL 终态存储拓扑校验失败 phase={Phase} reason={Reason} tenantScoped={TenantScoped}",
                phase,
                reason,
                tenantScoped);
        }
        else
        {
            _logger.LogCritical(
                "ETL 终态存储拓扑探测失败 phase={Phase} reason={Reason} tenantScoped={TenantScoped} exceptionType={ExceptionType}",
                phase,
                reason,
                tenantScoped,
                exceptionType.FullName ?? exceptionType.Name);
        }

        // 禁止附带 resolver 异常或连接串；调用方只得到稳定 503 契约并保留原 RunId。
        throw new BusinessException(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionCoordinationUnavailable);
    }

    private static string GetPhaseName(
        PipelineTerminalStoreTopologyPhase phase)
        => phase switch
        {
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission =>
                "execution_admission",
            PipelineTerminalStoreTopologyPhase.Claim =>
                "claim",
            PipelineTerminalStoreTopologyPhase.TerminalFinalize =>
                "terminal_finalize",
            PipelineTerminalStoreTopologyPhase.NotificationSchedule =>
                "notification_schedule",
            PipelineTerminalStoreTopologyPhase.OutcomeResolution =>
                "outcome_resolution",
            PipelineTerminalStoreTopologyPhase.RunningRecovery =>
                "running_recovery",
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification =>
                "target_receipt_verification",
            PipelineTerminalStoreTopologyPhase.TargetReceiptActivation =>
                "target_receipt_activation",
            PipelineTerminalStoreTopologyPhase.OutcomeResolutionApplied =>
                "outcome_resolution_applied",
            _ => throw new ArgumentOutOfRangeException(
                nameof(phase),
                phase,
                null),
        };

    private static PipelineTerminalStoreTopologyCapability
        GetRequiredCapabilities(PipelineTerminalStoreTopologyPhase phase)
        => phase switch
        {
            PipelineTerminalStoreTopologyPhase.TargetReceiptVerification or
            PipelineTerminalStoreTopologyPhase.TargetReceiptActivation or
            PipelineTerminalStoreTopologyPhase.OutcomeResolutionApplied =>
                PipelineTerminalStoreTopologyCapability.HostDefault,
            PipelineTerminalStoreTopologyPhase.ExecutionAdmission or
            PipelineTerminalStoreTopologyPhase.Claim or
            PipelineTerminalStoreTopologyPhase.TerminalFinalize or
            PipelineTerminalStoreTopologyPhase.NotificationSchedule or
            PipelineTerminalStoreTopologyPhase.OutcomeResolution or
            PipelineTerminalStoreTopologyPhase.RunningRecovery =>
                PipelineTerminalStoreTopologyCapability.HostDefault |
                PipelineTerminalStoreTopologyCapability.BackgroundJobs,
            _ => throw new ArgumentOutOfRangeException(
                nameof(phase),
                phase,
                null),
        };

    private sealed class ConnectionPin(
        string connectionString,
        PipelineTerminalStoreTopologyCapability capabilities) :
        IPipelineTerminalStoreTopologyPin
    {
        private int _consumed;

        public bool TryConsume(
            out string value,
            out PipelineTerminalStoreTopologyCapability capturedCapabilities)
        {
            if (Interlocked.Exchange(ref _consumed, 1) != 0)
            {
                value = string.Empty;
                capturedCapabilities = default;
                return false;
            }

            value = connectionString;
            capturedCapabilities = capabilities;
            return true;
        }
    }
}
