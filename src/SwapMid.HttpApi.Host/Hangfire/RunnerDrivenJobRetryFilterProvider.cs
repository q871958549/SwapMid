using System.Collections.Generic;
using Hangfire;
using Hangfire.Common;

namespace SwapMid.Hangfire;

/// <summary>
/// 给 Runner 驱动作业及显式标记的非 Runner ABP adapter 提供方法级零重试策略。
/// Method scope 会覆盖 Hangfire 默认的全局 AutomaticRetry(10)，防止非幂等任务失败后自动重放副作用。
/// </summary>
internal sealed class RunnerDrivenJobRetryFilterProvider : IJobFilterProvider
{
    private static readonly AutomaticRetryAttribute NoRetry = new()
    {
        Attempts = 0,
        LogEvents = false,
    };

    public IEnumerable<JobFilter> GetFilters(Job job)
    {
        if (RunnerDrivenHangfireJobClassifier.RequiresZeroAutomaticRetry(job))
        {
            yield return new JobFilter(NoRetry, JobFilterScope.Method, order: null);
        }
    }
}
