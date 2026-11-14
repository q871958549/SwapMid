using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Notification;

namespace SwapMid.Scheduling;

/// <summary>
/// 定时任务处理器契约：一个 JobKey 对应一个 handler，由 ScheduledJobRunner 统一驱动
/// （记执行历史 + 失败告警 + 产出物分发）。实现类标 ITransientDependency 自动注册收集。
/// </summary>
public interface IScheduledJobHandler
{
    /// <summary>任务唯一键，须与 ScheduledJobCatalog 注册的 key 一致。</summary>
    string JobKey { get; }

    /// <summary>
    /// 带参 / 演练执行支持的参数定义（前端据此动态渲染表单，替代硬编码字段）。
    /// 无参任务不必重写，默认空——ArgsJson 由本 handler 自行解析，schema 只是「说明书」，
    /// 定义与解析永远同处一处，杜绝「前端表单字段名跟 handler 实际读取的 key 对不上」。
    /// </summary>
    IReadOnlyList<JobParameterSchema> Parameters => Array.Empty<JobParameterSchema>();

    /// <summary>
    /// 互斥组名（R2 并发加固，蓝图 §20.3）：多个 <see cref="JobKey"/> 不同的 Handler 若业务上互斥
    /// （如主数据全量与增量两条同步任务不可同时全量执行，避免同一批记录被两侧并发创建撞唯一索引、
    /// 拖垮外层事务），可覆写为同一个组名——<c>ScheduledJobRunner</c> 的并发闸届时按组名而非各自
    /// JobKey 加锁，命中同组的第二个执行者维持既有「跳过并记历史」语义（不排队，避免 Hangfire 线程挂起）。
    /// 默认 null = 沿用旧行为（闸 key 退化为自身 JobKey，只防「同任务手动撞定时」）。
    /// </summary>
    string? MutexGroup => null;

    /// <summary>
    /// Runner 是否为处理器包裹数据库事务。默认保持既有事务语义；纯扫描、外呼或文件系统作业可关闭，
    /// 避免长时间持有数据库事务，也避免让不可回滚的外部副作用产生“可随事务回滚”的错觉。
    /// </summary>
    bool IsTransactional => true;

    /// <summary>
    /// Runner 是否为处理器创建 ambient UoW。默认保持既有行为；设为 false 的处理器必须自行划分
    /// 所有数据库短事务，并保证入口无父 UoW，适用于需要在事务外执行文件或网络副作用的协调器。
    /// </summary>
    bool UseUnitOfWork => true;

    /// <summary>执行任务，返回结果（状态 / 摘要 / 可选产出物）。</summary>
    Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken = default);
}

/// <summary>一个带参执行字段的声明：前端按此渲染输入控件、拼进 ArgsJson 时用 Name 作为 JSON key。</summary>
public class JobParameterSchema
{
    /// <summary>JSON 字段名（ArgsJson 里的 key），如 "month"、"date"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>中文标签，如「月份」「补刷日期」。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>字段类型，决定前端渲染的输入控件。</summary>
    public JobParameterType Type { get; set; }

    /// <summary>是否必填；false 时前端留空即不传该字段，由 handler 走默认值。</summary>
    public bool Required { get; set; }

    /// <summary>留空时的提示文案，如「默认上月」「默认昨天」。</summary>
    public string? DefaultHint { get; set; }
}

/// <summary>执行上下文：触发方式 + 演练开关 + 带参 + 测试收件人。</summary>
public class JobRunContext
{
    /// <summary>补偿参数 JSON 的统一字符上限；入口与 Runner 必须共同守住，兼容存量后台 payload。</summary>
    public const int MaxArgsJsonCharacters = 1024;

    /// <summary>触发方式（定时 / 手动）。</summary>
    public JobTriggerKind TriggerKind { get; init; }

    /// <summary>
    /// 演练模式：禁止业务写入；涉及外部依赖的处理器仅校验参数与配置，不访问外部数据源。
    /// 本地只读预览由处理器按任务策略决定；如返回产出物，Runner 也只允许发给测试收件人。
    /// </summary>
    public bool IsDryRun { get; init; }

    /// <summary>带参补偿参数（JSON），如 {"month":"2026-06"}；null = 按默认。</summary>
    public string? ArgsJson { get; init; }

    /// <summary>演练模式的测试收件人邮箱。</summary>
    public string? TestRecipient { get; init; }
}

/// <summary>执行结果：状态 + 摘要 + 可选产出物。</summary>
public class JobRunResult
{
    public JobExecutionStatus Status { get; init; }
    public string? Summary { get; init; }
    public JobOutput? Output { get; init; }

    /// <summary>成功结果（可带产出物用于分发）。</summary>
    public static JobRunResult Ok(string? summary, JobOutput? output = null)
        => new() { Status = JobExecutionStatus.Succeeded, Summary = summary, Output = output };

    /// <summary>失败结果。</summary>
    public static JobRunResult Fail(string? summary)
        => new() { Status = JobExecutionStatus.Failed, Summary = summary };

    /// <summary>因并发互斥等预期竞争未取得执行权；不触发失败告警、产出分发或恢复通知。</summary>
    public static JobRunResult Contended(string? summary)
        => new() { Status = JobExecutionStatus.Skipped, Summary = summary };
}

/// <summary>产出物：标题 / 正文 + 附件 + 分发来源（正式模式按来源匹配订阅接收人）。</summary>
public class JobOutput
{
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }

    /// <summary>正式广播时用于匹配 NotificationRecipient 的订阅来源。</summary>
    public string? DistributionSource { get; set; }

    /// <summary>产出附件（如 Excel）；类型与 NotificationSendRequest.Attachments 一致。</summary>
    public List<NotificationAttachment> Attachments { get; set; } = new();
}
