namespace SwapMid.Scheduling;

/// <summary>
/// 标记接口：此 Hangfire 入口经 <c>ScheduledJobRunner</c> 驱动、由 Runner 自记执行历史（带 jobKey + 业务摘要）。
/// <c>ScheduledJobExecutionFilter</c> 检测到本标记即跳过记录，避免同一次执行被 filter 与 Runner 双重记录。
/// 迁新任务到 Runner 时，其 Hangfire 入口类实现本接口即可，无需再手动维护 filter 的排除名单（替代硬编码字符串）。
/// </summary>
public interface IRunnerDrivenJob
{
}
