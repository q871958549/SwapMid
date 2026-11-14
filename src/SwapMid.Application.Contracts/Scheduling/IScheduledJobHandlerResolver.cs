using System.Collections.Generic;

namespace SwapMid.Scheduling;

/// <summary>
/// 按 JobKey 逐个隔离解析 <see cref="IScheduledJobHandler"/>。
///
/// 存在的理由是**故障隔离**：直接注入 <c>IEnumerable&lt;IScheduledJobHandler&gt;</c> 会让容器在解析集合时
/// 一次性构造全部处理器，于是任何一个处理器的依赖链断裂（缺注册、构造期建立外部连接、配置缺失）都会让
/// 整个集合解析抛异常——一个任务的问题连坐掉全部 Runner 驱动任务。更糟的是该异常发生在
/// <see cref="IScheduledJobHandler"/> 被调用**之前**，即落在 <c>ScheduledJobRunner</c> 的 try 之外，
/// 绕过其「记执行历史 + 失败告警」保护圈，管控页上不留任何痕迹，只剩一条 Hangfire 告警说「某任务失败了」。
///
/// 本解析器逐个隔离构造：坏的那个只记 Warning 并跳过，其余任务照常运行。
/// </summary>
public interface IScheduledJobHandlerResolver
{
    /// <summary>
    /// 解析指定 JobKey 的处理器；找不到（未注册、<c>[ExposeServices]</c> 漏标，或该处理器自身构造失败）
    /// 返回 null，由调用方决定语义——不在此抛异常，避免又把失败甩回 Runner 保护圈之外。
    /// </summary>
    IScheduledJobHandler? Find(string jobKey);

    /// <summary>
    /// 解析全部**可成功构造**的处理器，构造失败的被跳过（不影响其余）。
    /// 供管控页列举任务参数 schema：单个处理器坏死不应让整个定时任务列表页打不开。
    /// </summary>
    IReadOnlyList<IScheduledJobHandler> ResolveAvailable();
}
