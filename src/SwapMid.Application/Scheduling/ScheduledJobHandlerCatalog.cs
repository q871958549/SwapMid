using System;
using System.Collections.Generic;

namespace SwapMid.Scheduling;

/// <summary>
/// 全部注册为 <see cref="IScheduledJobHandler"/> 的实现类型清单（进程级单例）。
///
/// 清单在 <c>SwapMidApplicationModule.ConfigureServices</c> 里直接从
/// <c>IServiceCollection</c> 抓取：**DI 注册本身就是事实源**，不靠反射猜「哪些类型算处理器」。
/// 因此 <c>[ExposeServices(typeof(IScheduledJobHandler), …)]</c> 漏标仍会照旧表现为「找不到该 JobKey」，
/// 就近 CLAUDE.md 记录的那个坑不会被本机制掩盖成「反正扫描得到」。
/// </summary>
public sealed class ScheduledJobHandlerCatalog
{
    /// <summary>处理器实现类型（可直接按具体类型从容器解析——约定注册同时暴露了自身类型）。</summary>
    public IReadOnlyList<Type> HandlerTypes { get; }

    public ScheduledJobHandlerCatalog(IReadOnlyList<Type> handlerTypes)
    {
        // 空清单 = 抓取时机早于 ABP 约定注册，或注册方式被改动。这属于「部署即死」的配置错误：
        // 静默放行会让全部 Runner 驱动任务退化成运行时的「未找到 handler」，比启动失败难查得多。
        if (handlerTypes.Count == 0)
        {
            throw new InvalidOperationException(
                "未发现任何 IScheduledJobHandler 注册，全部定时任务都将无法执行。" +
                "请检查 SwapMidApplicationModule 抓取处理器清单的时机是否早于 ABP 约定注册。");
        }

        HandlerTypes = handlerTypes;
    }
}
