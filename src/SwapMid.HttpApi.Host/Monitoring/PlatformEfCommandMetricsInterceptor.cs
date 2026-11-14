using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

// 与 Application 层的 PlatformMetrics 同命名空间（本仓 Monitoring 惯例，见 SwapMidCompositeSampler）。
namespace SwapMid.Monitoring;

/// <summary>
/// 全局 EF Core 命令观测：为**每一条**数据库命令记录耗时与成败，并把超阈值的慢查询打进日志。
///
/// ## 为什么需要它
/// 各域自己的指标（样品列表耗时、批量耗时…）只覆盖**被想到要埋点**的路径。而数据涨上来后
/// 真正拖慢系统的，往往恰恰是没人想到的那条——某个下拉框的全表扫、某个统计的 N+1、
/// 某个「反正数据不多」的历史假设被穿透。这一层是兜底：先用指标看出「哪个 DbContext 在变慢」，
/// 再用慢查询日志定位到具体 SQL。
///
/// 它也补上了同步终结符门禁（<c>SyncQueryTerminatorGuardTests</c>）刻意留下的漏洞：那个门禁
/// 为了零误报放弃了跨方法传递、lambda 内执行等场景，漏掉的部分由这里的运行时观测兜住。
///
/// ## 落点为什么在 Host
/// 它同时需要 <c>Microsoft.EntityFrameworkCore.Diagnostics</c>（拦截器基类）与 <c>PlatformMetrics</c>
/// （Application 层）。而 Application 层**禁止依赖 EF Core**（分层硬规范），EntityFrameworkCore 层
/// 又不依赖 Application——两边都放不下。Host 同时引用两者，横切能力接线本就归它。
///
/// ## 三条硬约束
/// - **指标标签只放 DbContext 简名与操作类型**（约 14 × 3）。SQL 文本基数无限、表名上百，
///   放进标签会撑爆 Prometheus 的时间序列存储。定位具体语句请看日志。
/// - **日志只记参数化 SQL 文本，绝不记参数值**。EF 的 <c>CommandText</c> 里参数是占位符
///   （<c>@p0</c>），真实值在 <c>command.Parameters</c> 里——那里可能有姓名、工号、手机号，
///   一旦进日志就是把生产数据抄进 Seq。本类从不读取 Parameters。
/// - **失败的命令必须一并统计**：被超时掐断的慢查询走不到「已执行」回调、只走失败回调，
///   而那正是最该被看见的一批。缺了它，最严重的问题在指标上完全隐身。
///
/// ## 口径局限（看指标时必须知道）
/// <c>eventData.Duration</c> 只计到**命令返回 / 首批结果就绪**，不含随后消费 DataReader
/// 拉完全部行的时间。因此大结果集（导出、报表）的真实耗时会被本指标低估。要覆盖全程需另行
/// 拦截 <c>DataReaderDisposing</c>，首版刻意不做——那会让每条查询多一次回调开销，而当前要
/// 回答的问题（哪个 DbContext 在变慢）用命令耗时已经够。
/// </summary>
public sealed class PlatformEfCommandMetricsInterceptor : DbCommandInterceptor, ISingletonDependency
{
    /// <summary>慢查询阈值配置键；缺省 1000ms。</summary>
    public const string ThresholdConfigurationKey = "Monitoring:EfSlowCommandThresholdMs";

    /// <summary>
    /// 默认 1 秒。依据：本平台列表查询配上索引后正常在百毫秒内，1 秒意味着要么索引没用上、
    /// 要么数据量已越过某个假设。更低（100ms）会让日志被正常报表刷屏，更高（5s）会漏掉
    /// 「还没到不可用、但已经在占线程」的那批——而后者正是雪崩的前兆。
    /// </summary>
    private const int DefaultThresholdMilliseconds = 1000;

    /// <summary>日志里 SQL 的截断长度。慢查询多半出在复杂语句上，全文进日志会把 Seq 撑爆。</summary>
    private const int SqlLogMaxLength = 800;

    private readonly ILogger<PlatformEfCommandMetricsInterceptor> _logger;
    private readonly TimeSpan _slowThreshold;

    public PlatformEfCommandMetricsInterceptor(
        ILogger<PlatformEfCommandMetricsInterceptor> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        // 构造时读一次即可：本类是 singleton，且每条 SQL 都要比对阈值——放进热路径去查配置
        // （更别说 ABP Setting 那种带缓存查询的）会让兜底观测本身变成开销来源。
        var configured = configuration.GetValue<int?>(ThresholdConfigurationKey);
        _slowThreshold = TimeSpan.FromMilliseconds(
            configured is > 0 ? configured.Value : DefaultThresholdMilliseconds);
    }

    // ── 已执行（异步路径，应当是绝大多数）────────────────────────────────────────────────

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "reader");
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "non_query");
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "scalar");
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    // ── 已执行（同步路径）────────────────────────────────────────────────────────────────
    // **必须一并覆盖**：仓内尚有 223 处已登记的同步终结符（sync-query-terminator-baseline.txt），
    // 它们恰恰最该被观测——同步调用阻塞线程池线程，慢起来的破坏力比异步大得多。
    // 每条命令只会走同步或异步其中之一，不存在双计。

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData, "reader");
        return base.ReaderExecuted(command, eventData, result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData, "non_query");
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData, "scalar");
        return base.ScalarExecuted(command, eventData, result);
    }

    // ── 执行失败 ────────────────────────────────────────────────────────────────────────
    // 超时掐断的慢查询只走这里，不走 Executed（见类注释第三条硬约束）。

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        RecordFailure(eventData);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RecordFailure(eventData);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    private void Record(DbCommand command, CommandExecutedEventData eventData, string operation)
    {
        var context = ResolveContextLabel(eventData.Context);
        var duration = eventData.Duration;

        PlatformMetrics.EfCommandDuration.WithLabels(context, operation).Observe(duration.TotalSeconds);

        if (duration < _slowThreshold)
        {
            return;
        }

        PlatformMetrics.EfSlowCommands.WithLabels(context, operation).Inc();

        // ⚠️ 只记 CommandText（参数是 @p0 占位符），绝不碰 command.Parameters——那里是真实业务值。
        _logger.LogWarning(
            "慢查询 {ElapsedMs}ms（阈值 {ThresholdMs}ms）· {Context} · {Operation} · SQL: {Sql}",
            (long)duration.TotalMilliseconds, (long)_slowThreshold.TotalMilliseconds,
            context, operation, Truncate(command.CommandText));
    }

    private void RecordFailure(CommandErrorEventData eventData)
    {
        // 失败只计数、不在这里记日志：EF 自己会把命令异常记进日志，重复一遍只是噪音。
        // 指标的价值在于「能被告警规则消费」，那是日志给不了的。
        PlatformMetrics.EfCommandsFailed
            .WithLabels(ResolveContextLabel(eventData.Context), ResolveOperation(eventData))
            .Inc();
    }

    /// <summary>
    /// DbContext 为空的场景（迁移工具、裸 ADO 命令）归到 unknown，**仍然计入**——
    /// 观测缺口比标签不精确危险得多。标签值也绝不能是空串（Prometheus 里空标签难以区分「没有」和「没记」）。
    /// </summary>
    private static string ResolveContextLabel(Microsoft.EntityFrameworkCore.DbContext? context)
        => context?.GetType().Name ?? "unknown";

    private static string ResolveOperation(CommandErrorEventData eventData)
        => eventData.ExecuteMethod switch
        {
            DbCommandMethod.ExecuteReader => "reader",
            DbCommandMethod.ExecuteNonQuery => "non_query",
            DbCommandMethod.ExecuteScalar => "scalar",
            _ => "unknown",
        };

    private static string Truncate(string sql)
        => sql.Length <= SqlLogMaxLength
            ? sql
            : string.Concat(sql.AsSpan(0, SqlLogMaxLength), " …（已截断）");
}
