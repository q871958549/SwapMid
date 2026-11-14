using System;
using System.Threading.Tasks;
using SwapMid.Monitoring;
using SwapMid.Notification;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SwapMid.Hangfire;

/// <summary>
/// Hangfire 全局过滤器：任何后台作业最终失败（重试耗尽进 FailedState）时，经通用通知中心告警。
/// 一处覆盖所有 Hangfire 作业（OA 同步 / 材料超耗 / 库存同步 / 项目同步 / 消息投递…）。
/// 通知是旁路：用独立 scope 异步发送，异常吞掉，绝不影响 Hangfire 作业状态机。
/// 注：重试中途的失败由 AutomaticRetry 转为 ScheduledState，不会进这里——只有最终失败才告警。
/// </summary>
public class JobFailureNotificationFilter : IApplyStateFilter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobFailureNotificationFilter> _logger;

    public JobFailureNotificationFilter(IServiceScopeFactory scopeFactory, ILogger<JobFailureNotificationFilter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState failed)
            return;

        var jobType = context.BackgroundJob?.Job?.Type?.Name ?? "Hangfire 作业";
        var method = context.BackgroundJob?.Job?.Method?.Name ?? "";
        var jobId = context.BackgroundJob?.Id ?? "";
        var error = ScheduledJobRecordedFailureInspector.DescribeLeaves(failed.Exception);

        // 调用方取消不是业务失败：不计失败指标、不发告警。
        if (ScheduledJobRecordedFailureInspector.AllLeavesAreCancellation(failed.Exception))
        {
            return;
        }

        // 业务指标：Hangfire 作业最终失败计数（Prometheus /metrics 暴露，与通知告警互补——指标看趋势/告警看即时）
        PlatformMetrics.HangfireJobsFailed.WithLabels(jobType).Inc();

        // Runner 的终态落库与告警送达是两个独立事实。只有告警已发送，或被明确的演练/
        // 连续失败/未配置渠道策略处理时才抑制 fallback；真实发送失败必须再尝试通用告警。
        if (ScheduledJobRecordedFailureInspector.AllFailureNotificationsAreHandled(
                failed.Exception))
        {
            return;
        }

        // fire-and-forget：独立 scope 异步发送，不阻塞 Hangfire worker；任何异常吞掉
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var notification = await notificationService.SendAsync(new NotificationSendRequest
                {
                    Title = $"后台作业失败：{jobType}",
                    Content = $"作业：{jobType}.{method}\n作业ID：{jobId}\n错误：{error}",
                    Level = NotificationLevel.Critical,
                    Source = NotificationSources.Hangfire,
                });
                if (!notification.Success && !notification.Skipped)
                {
                    _logger.LogWarning(
                        "Hangfire 作业失败通知未送达 jobId={JobId}: {Error}",
                        jobId,
                        notification.Error ?? "未知通知错误");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hangfire 作业失败通知发送异常 jobId={JobId}", jobId);
            }
        });
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
