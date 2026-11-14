using System;
using System.Diagnostics;
using SwapMid.Monitoring;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.Logging;

namespace SwapMid.Hangfire;

/// <summary>
/// 为每个 Hangfire 后台作业起一个「关键链路」根 <see cref="Activity"/>（ActivityKind.Server），
/// 避免后台 worker 线程内的 DB/HTTP 自动 span 成为孤儿 trace。
/// </summary>
public sealed class HangfireTracingFilter : JobFilterAttribute, IServerFilter
{
    private const string ActivityKey = "swapmid.otel.activity";

    private readonly ILogger<HangfireTracingFilter> _logger;

    public HangfireTracingFilter(ILogger<HangfireTracingFilter> logger)
    {
        _logger = logger;
    }

    public void OnPerforming(PerformingContext context)
    {
        try
        {
            var job = context.BackgroundJob.Job;
            var activity = SwapMidTracing.StartCritical($"hangfire {job.Type.Name}.{job.Method.Name}", ActivityKind.Server);
            if (activity != null)
            {
                activity.SetTag("hangfire.job_id", context.BackgroundJob.Id);
                activity.SetTag("hangfire.job_type", job.Type.Name);
                context.Items[ActivityKey] = activity;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hangfire 作业链路追踪 OnPerforming 失败（不影响作业执行）");
        }
    }

    public void OnPerformed(PerformedContext context)
    {
        try
        {
            if (context.Items.TryGetValue(ActivityKey, out var obj) && obj is Activity activity)
            {
                if (context.Exception != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, context.Exception.Message);
                }

                activity.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hangfire 作业链路追踪 OnPerformed 失败（不影响作业执行）");
        }
    }
}
