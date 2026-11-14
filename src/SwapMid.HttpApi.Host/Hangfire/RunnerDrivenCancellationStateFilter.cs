using Hangfire.Common;
using Hangfire.States;

namespace SwapMid.Hangfire;

/// <summary>
/// 将已经进入状态选举的纯 Runner 取消 marker 精确改选为 Deleted。
/// worker shutdown 时 marker 保持 OCE，Hangfire 会在状态选举前重新入队；只有独立调用方取消
/// 才会到这里。混合 Aggregate、原始 OCE 与任何业务异常都保留原候选，避免吞掉真实故障。
/// </summary>
internal sealed class RunnerDrivenCancellationStateFilter : JobFilterAttribute, IElectStateFilter
{
    public RunnerDrivenCancellationStateFilter()
    {
        // 必须早于 Hangfire AutomaticRetryAttribute（Order=20）处理候选状态。
        Order = -1;
    }

    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not FailedState failed
            || !ScheduledJobRecordedFailureInspector.AllLeavesAreCancellationMarkers(
                failed.Exception))
        {
            return;
        }

        context.CandidateState = new DeletedState
        {
            Reason = "调用方取消；平台按 Interrupted 语义处理执行历史",
        };
    }
}
