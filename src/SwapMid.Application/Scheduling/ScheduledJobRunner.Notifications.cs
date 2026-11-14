using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.Notification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Uow;

namespace SwapMid.Scheduling;

public partial class ScheduledJobRunner
{
    /// <summary>产出物分发结果：Error 非空 = 真失败（渠道启用但发送异常，调用方记 Failed + 告警）；
    /// SkipReason 非空 = 配置性跳过（渠道未启用 / 无订阅接收人 / 演练未填测试收件人，记摘要不算失败）；
    /// 两者皆空 = 发送成功或无产出可分发。</summary>
    private readonly record struct DistributeOutcome(string? Error, string? SkipReason)
    {
        public static DistributeOutcome NotAttempted => new(null, null);
        public static DistributeOutcome Sent => new(null, null);
        public static DistributeOutcome Fail(string error) => new(error, null);
        public static DistributeOutcome Skip(string reason) => new(null, reason);
    }

    private async Task<DistributeOutcome> DistributeAsync(JobOutput output, JobRunContext context)
    {
        try
        {
            var request = new NotificationSendRequest
            {
                Title = output.Title,
                Content = output.Body ?? string.Empty,
                Level = NotificationLevel.Info,
                Channel = NotificationChannelType.Email, // 附件仅邮件渠道
                Attachments = output.Attachments,
            };

            if (context.IsDryRun)
            {
                // 演练：只发测试收件人（定向），不触达正式订阅人。未填收件人是用户操作提示、非系统故障。
                if (string.IsNullOrWhiteSpace(context.TestRecipient))
                {
                    _logger.LogWarning("演练模式但未提供测试收件人，跳过分发：{Title}", output.Title);
                    return DistributeOutcome.Skip("演练未填写测试收件人");
                }
                request.ToUser = context.TestRecipient;
                request.Title = "[演练] " + request.Title;
            }
            else
            {
                // 正式：广播给订阅 DistributionSource 来源的接收人（ToUser 留空 = 广播）。
                request.Source = output.DistributionSource;
            }

            var r = await _notificationService.SendAsync(request);
            if (r.Success)
            {
                return DistributeOutcome.Sent;
            }

            if (r.Skipped)
            {
                _logger.LogInformation("产出物分发跳过 {Title}：{Reason}", output.Title, r.Error);
                return DistributeOutcome.Skip(r.Error ?? "通知渠道未启用或无订阅接收人");
            }

            _logger.LogWarning("产出物分发失败 {Title}：{Error}", output.Title, r.Error);
            return DistributeOutcome.Fail(r.Error ?? "分发发送失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "产出物分发异常：{Title}", output.Title);
            return DistributeOutcome.Fail(ex.Message);
        }
    }

    /// <summary>连续失败第 1 次与之后每第 N 次告警（N 可配 ScheduledJobAlert:RepeatEvery，默认 5），
    /// 其余抑制只记日志。基于执行历史统计（{JobKey, StartedAt} 已有索引、零新表）：本次失败记录已由
    /// FinishAsync 落库，故计数含本次——连续失败数 1 或 N 的倍数即告警。计数只看正式（非演练）终态记录，
    /// 封顶 N×8+1 条：超长连续失败进入静默期（此前已告警 ~9 次足以引起注意），恢复时有恢复通知兜底。</summary>
    private async Task<bool> TryNotifyFailureWithSuppressionAsync(string jobKey, Guid failedExecutionId, string? error)
    {
        var repeatEvery = Math.Max(1, _configuration.GetValue("ScheduledJobAlert:RepeatEvery", 5));

        try
        {
            var consecutive = Math.Max(1, await CountConsecutiveFailuresAsync(jobKey, cap: repeatEvery * 8 + 1));
            if (consecutive != 1 && consecutive % repeatEvery != 0)
            {
                _logger.LogInformation("定时任务 {JobKey} 连续第 {Count} 次失败，告警已抑制（首次与每第 {N} 次告警）",
                    jobKey, consecutive, repeatEvery);
                return true;
            }

            // 本次失败已经由记录器确认。历史读取暂缺时仍告警，不能让恢复关联的证据要求阻断故障通知。
            // 按确切执行 Id 读取，不能把另一实例刚写入的最新执行当成本次失败。
            ScheduledJobExecution? failure;
            using (var lookupUow = _uowManager.Begin(requiresNew: true, isTransactional: false))
            {
                failure = await _executionRepository.FindAsync(failedExecutionId);
                await lookupUow.CompleteAsync();
            }
            var incidentId = failure is { Status: JobExecutionStatus.Failed, IsDryRun: false } && failure.JobKey == jobKey
                ? await FindIncidentStartAsync(jobKey, failure)
                : failedExecutionId;
            var notification = await _notificationService.SendAsync(new NotificationSendRequest
            {
                Title = FailureTitle(jobKey, incidentId),
                Content = $"连续失败 {consecutive} 次。事故标识：{incidentId:N}。\n{error ?? "未知错误"}",
                Level = NotificationLevel.Critical,
                Source = NotificationSources.ScheduledJobs,
            });
            if (notification.Success || notification.Skipped)
            {
                return true;
            }

            _logger.LogWarning(
                "定时任务失败告警未送达 {JobKey}: {Error}；允许 Hangfire fallback 再尝试",
                jobKey,
                notification.Error ?? "未知通知错误");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定时任务失败告警发送异常 {JobKey}", jobKey);
            return false;
        }
    }

    /// <summary>本次成功、且上一条正式终态记录是失败 → 发 Info 恢复通知（闭合告警区间，运维不用翻历史确认是否修好）。</summary>
    private async Task SafeNotifyRecoveryIfNeededAsync(
        string jobKey,
        Guid succeededExecutionId)
    {
        try
        {
            // 本次 Succeeded 记录已落库为最新一条，上一条 = 终态列表的第 2 条。
            var recent = await GetRecentFinalExecutionsAsync(jobKey, take: 2);
            if (recent.Count < 2
                || recent[0].Id != succeededExecutionId
                || recent[0].Status != JobExecutionStatus.Succeeded
                || recent[1].Status != JobExecutionStatus.Failed)
            {
                return;
            }

            var incidentId = await FindIncidentStartAsync(jobKey, recent[1]);
            var delivery = await _notificationService.SendAsync(new NotificationSendRequest
            {
                Title = $"定时任务已恢复：{jobKey}（事故：{incidentId:N}）",
                Content = $"本次正式执行已成功。事故标识：{incidentId:N}；成功执行：{succeededExecutionId:N}。恢复仅代表本次执行成功，不证明历史业务通知已补送。",
                Level = NotificationLevel.Info,
                Source = NotificationSources.ScheduledJobs,
                RecoveryOfTitle = FailureTitle(jobKey, incidentId),
            });
            if (!delivery.Success)
                _logger.LogWarning("定时任务 {JobKey} 已成功，但恢复通知未送达或无原故障受众：{Reason}", jobKey, delivery.Error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定时任务恢复通知发送异常 {JobKey}", jobKey);
        }
    }

    private static string FailureTitle(string jobKey, Guid incidentId)
        => $"定时任务失败：{jobKey}（事故：{incidentId:N}）";

    // 利用现有执行台账定位同一失败区间的首条记录；只取两个边界，不加载整段失败历史。
    private async Task<Guid> FindIncidentStartAsync(string jobKey, ScheduledJobExecution failure)
    {
        using var uow = _uowManager.Begin(requiresNew: true, isTransactional: false);
        var query = await _executionRepository.GetQueryableAsync();
        if (await _asyncExecuter.AnyAsync(query.Where(e => e.JobKey == jobKey && !e.IsDryRun
                && e.Status == JobExecutionStatus.Succeeded && e.StartedAt == failure.StartedAt)))
            throw new InvalidOperationException("任务成败记录的开始时间相同，无法可靠关联事故");
        var previousSuccess = await _asyncExecuter.FirstOrDefaultAsync(query
            .Where(e => e.JobKey == jobKey && !e.IsDryRun && e.Status == JobExecutionStatus.Succeeded
                && e.StartedAt < failure.StartedAt)
            .OrderByDescending(e => e.StartedAt).ThenByDescending(e => e.Id));
        if (previousSuccess != null && await _asyncExecuter.AnyAsync(query.Where(e => e.JobKey == jobKey
                && !e.IsDryRun && e.Status == JobExecutionStatus.Failed && e.StartedAt == previousSuccess.StartedAt)))
            throw new InvalidOperationException("任务事故边界存在同时刻成败记录，拒绝混并事故");
        var failed = query.Where(e => e.JobKey == jobKey && !e.IsDryRun
            && e.Status == JobExecutionStatus.Failed && e.StartedAt <= failure.StartedAt);
        if (previousSuccess != null)
            failed = failed.Where(e => e.StartedAt > previousSuccess.StartedAt);
        var first = await _asyncExecuter.FirstOrDefaultAsync(failed.OrderBy(e => e.StartedAt).ThenBy(e => e.Id));
        await uow.CompleteAsync();
        return first?.Id ?? failure.Id;
    }

    // 从最新一条起数连续 Failed 的条数（含本次），遇到 Succeeded 即停；cap 封顶防全表扫。
    private async Task<int> CountConsecutiveFailuresAsync(string jobKey, int cap)
    {
        var recent = await GetRecentFinalExecutionsAsync(jobKey, cap);
        var count = 0;
        foreach (var e in recent)
        {
            if (e.Status != JobExecutionStatus.Failed)
            {
                break;
            }
            count++;
        }

        return count;
    }
}
