using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;
using Volo.Abp;

namespace SwapMid.DataIntegration;

[Flags]
internal enum PipelineTerminalStoreTopologyCapability
{
    HostDefault = 1,
    BackgroundJobs = 2,
}

/// <summary>
/// 保存单次 ETL 执行已验证的 host Default 原始连接串及可选 BackgroundJobs
/// 能力。值只存在于当前异步调用链，不进入实体、日志、指标或异常；作用域退出时
/// 会同时清空所有继承该 holder 的子上下文。
/// </summary>
public sealed class PipelineTerminalStoreConnectionScopeAccessor
{
    private readonly AsyncLocal<ScopeHolder?> _current = new();

    internal bool IsActive
    {
        get
        {
            var holder = _current.Value;
            return holder != null &&
                   !Volatile.Read(ref holder.Revoked) &&
                   !string.IsNullOrWhiteSpace(
                       Volatile.Read(ref holder.ConnectionString));
        }
    }

    internal bool HasScopeHolder => _current.Value != null;

    internal bool Supports(
        PipelineTerminalStoreTopologyCapability requiredCapabilities)
    {
        var holder = _current.Value;
        return holder != null &&
               !Volatile.Read(ref holder.Revoked) &&
               (holder.Capabilities & requiredCapabilities) ==
               requiredCapabilities &&
               !string.IsNullOrWhiteSpace(
                   Volatile.Read(ref holder.ConnectionString));
    }

    internal bool TryResolve(
        string? connectionStringName,
        out string connectionString)
    {
        var holder = _current.Value;
        if (holder != null && IsPinnedConnectionName(connectionStringName))
        {
            if (Volatile.Read(ref holder.Revoked))
            {
                throw new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionCoordinationUnavailable);
            }

            var pinned = Volatile.Read(ref holder.ConnectionString);
            if (string.IsNullOrWhiteSpace(pinned))
            {
                throw new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionCoordinationUnavailable);
            }

            var requiredCapability = IsBackgroundJobsConnectionName(
                connectionStringName)
                ? PipelineTerminalStoreTopologyCapability.BackgroundJobs
                : PipelineTerminalStoreTopologyCapability.HostDefault;
            if ((holder.Capabilities & requiredCapability) !=
                requiredCapability)
            {
                // host-only pin 绝不能静默回退到动态 BackgroundJobs 配置；否则
                // 通知写入会逃出已验证的终态事务拓扑。
                throw new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionCoordinationUnavailable);
            }

            connectionString = pinned;
            return true;
        }

        connectionString = string.Empty;
        return false;
    }

    internal IDisposable Enter(string connectionString)
        => Enter(
            connectionString,
            PipelineTerminalStoreTopologyCapability.HostDefault |
            PipelineTerminalStoreTopologyCapability.BackgroundJobs);

    internal IDisposable Enter(
        string connectionString,
        PipelineTerminalStoreTopologyCapability capabilities)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A terminal-store connection is required.",
                nameof(connectionString));
        }

        if (IsActive)
        {
            throw new InvalidOperationException(
                "A terminal-store connection scope is already active.");
        }

        const PipelineTerminalStoreTopologyCapability allCapabilities =
            PipelineTerminalStoreTopologyCapability.HostDefault |
            PipelineTerminalStoreTopologyCapability.BackgroundJobs;
        if ((capabilities &
             PipelineTerminalStoreTopologyCapability.HostDefault) == 0 ||
            (capabilities & ~allCapabilities) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capabilities),
                capabilities,
                "A terminal-store scope must pin Host Default and only known capabilities.");
        }

        var holder = new ScopeHolder
        {
            ConnectionString = connectionString,
            Capabilities = capabilities,
        };
        _current.Value = holder;
        return new Scope(this, holder);
    }

    private void Exit(ScopeHolder holder)
    {
        // AsyncLocal 会把 holder 引用复制给子 ExecutionContext。先清空 holder 内容，
        // 可避免调用方误启动的延迟任务在父作用域退出后继续继承凭据。
        Volatile.Write(ref holder.Revoked, true);
        Interlocked.Exchange(ref holder.ConnectionString, null);
        if (ReferenceEquals(_current.Value, holder))
        {
            _current.Value = null;
        }
    }

    private static bool IsPinnedConnectionName(string? name)
        => name == null ||
           string.Equals(
               name,
               ConnectionStrings.DefaultConnectionStringName,
               StringComparison.Ordinal) ||
           string.Equals(
               name,
                AbpBackgroundJobsDbProperties.ConnectionStringName,
                StringComparison.Ordinal);

    private static bool IsBackgroundJobsConnectionName(string? name)
        => string.Equals(
            name,
            AbpBackgroundJobsDbProperties.ConnectionStringName,
            StringComparison.Ordinal);

    private sealed class ScopeHolder
    {
        public string? ConnectionString;
        public PipelineTerminalStoreTopologyCapability Capabilities;
        public bool Revoked;
    }

    private sealed class Scope(
        PipelineTerminalStoreConnectionScopeAccessor owner,
        ScopeHolder holder) : IDisposable
    {
        private PipelineTerminalStoreConnectionScopeAccessor? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Exit(holder);
        }
    }
}

/// <summary>
/// ABP 为 DbContext 建立 transaction key 时直接使用 resolver 返回的原始连接串。
/// ETL 一旦完成拓扑校验，本 resolver 会让 Platform、PG fence，以及声明了对应
/// 能力的 BackgroundJobs，在整个异步执行作用域内始终得到同一原始值，关闭配置
/// 热变导致的双快照 TOCTOU。host-only scope 请求 BackgroundJobs 时必须失败关闭。
/// </summary>
public sealed class PipelinePinnedConnectionStringResolver :
    MultiTenantConnectionStringResolver
{
    private readonly PipelineTerminalStoreConnectionScopeAccessor
        _scopeAccessor;

    public PipelinePinnedConnectionStringResolver(
        IOptionsMonitor<AbpDbConnectionOptions> options,
        ICurrentTenant currentTenant,
        IServiceProvider serviceProvider,
        PipelineTerminalStoreConnectionScopeAccessor scopeAccessor)
        : base(options, currentTenant, serviceProvider)
    {
        _scopeAccessor = scopeAccessor;
    }

    public override Task<string> ResolveAsync(
        string? connectionStringName = null)
        => _scopeAccessor.TryResolve(
            connectionStringName,
            out var connectionString)
            ? Task.FromResult(connectionString)
            : base.ResolveAsync(connectionStringName);

#pragma warning disable CS0672, CS0618 // ABP 仍保留同步 resolver 供同步 DbContext 路径使用。
    public override string Resolve(string? connectionStringName = null)
        => _scopeAccessor.TryResolve(
            connectionStringName,
            out var connectionString)
            ? connectionString
            : base.Resolve(connectionStringName);
#pragma warning restore CS0672, CS0618
}
