using System;
using System.Threading.Tasks;

namespace SwapMid.Scheduling;

/// <summary>执行历史起始记录的持久化结果；即使写入失败也保留预生成 id，供终态补偿复用。</summary>
public readonly record struct ScheduledJobExecutionStartResult(Guid ExecutionId, bool Recorded);

/// <summary>
/// 执行历史记录器（内部机制，非 AppService）：由 Host 层 Hangfire 过滤器在作业前后调用，
/// 各用独立 UoW 写入 <c>ScheduledJobExecution</c>，写入失败不影响作业本身。
/// </summary>
public interface IScheduledJobExecutionRecorder
{
    /// <summary>作业开始时插入一条 Running 记录，返回预生成 id 与提交确认。isDryRun/argsJson 老轨 Hangfire 过滤器留默认（无该概念）。</summary>
    Task<ScheduledJobExecutionStartResult> StartAsync(string? jobKey, string jobType, string? jobMethod, string? hangfireJobId, JobTriggerKind triggerKind, bool isDryRun = false, string? argsJson = null);

    /// <summary>作业结束时标记成败、耗时、摘要、错误；仅事务提交成功且记录存在时返回 true。</summary>
    Task<bool> FinishAsync(Guid executionId, bool success, string? summary, string? error);

    /// <summary>作业被调用方取消时标记为 Interrupted；仅事务提交成功且记录存在时返回 true。</summary>
    Task<bool> InterruptAsync(Guid executionId, string? summary = null);

    /// <summary>作业因预期竞争未取得执行权时标记为 Skipped；仅事务提交成功且记录存在时返回 true。</summary>
    Task<bool> SkipAsync(Guid executionId, string? summary = null);
}
