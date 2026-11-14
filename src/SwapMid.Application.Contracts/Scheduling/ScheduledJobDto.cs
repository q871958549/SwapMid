using System;
using System.Collections.Generic;

namespace SwapMid.Scheduling;

/// <summary>定时任务（Hangfire recurring job）的统一管控视图。</summary>
public class ScheduledJobDto
{
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string Cron { get; set; } = null!;

    /// <summary>是否已在 Hangfire 中注册（启用）。</summary>
    public bool Enabled { get; set; }

    /// <summary>是否已接入统一执行管道（IScheduledJobHandler），支持带参 / 演练执行——
    /// 全部受管控任务均已接入，无论启用与否都可据此「立即执行」（恒 true，字段保留供前端按接口收缩演进）。</summary>
    public bool SupportsParameterizedRun { get; set; }

    /// <summary>带参执行支持的参数定义（前端据此动态渲染表单）；未接入管道或无参任务为空列表。</summary>
    public List<JobParameterSchema> Parameters { get; set; } = new();

    /// <summary>危险操作（物理删除数据 / 写外部生产系统）：前端标红并在「立即执行」前二次确认。</summary>
    public bool IsDestructive { get; set; }

    /// <summary>长任务（实时查外部库的报表 / 全量同步，单次数十秒到分钟级）：前端「立即执行」默认走后台入队
    /// （RunInBackground），入队后到执行历史看进度；演练仍同步等待结果。</summary>
    public bool IsLongRunning { get; set; }

    /// <summary>
    /// 运行时互斥组；同组任务不会并发执行。空值表示仅按自身 JobKey 防止重复执行。
    /// 该字段只用于发布检查与管控页解释，不提供修改入口。
    /// </summary>
    public string? MutexGroup { get; set; }

    public DateTime? LastExecution { get; set; }
    public DateTime? NextExecution { get; set; }
    public string? LastJobState { get; set; }
}
