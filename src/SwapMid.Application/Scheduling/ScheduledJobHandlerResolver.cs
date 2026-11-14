using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Scheduling;

/// <summary>
/// <see cref="IScheduledJobHandlerResolver"/> 的实现：逐个隔离构造处理器，单个坏死不连坐其余任务。
/// 解析用当前 scope 的 <see cref="IServiceProvider"/>（处理器是 Transient 且依赖 Scoped 仓储，
/// 必须在作业自己的 scope 内构造），JobKey→类型索引则是进程级静态缓存。
/// </summary>
public class ScheduledJobHandlerResolver : IScheduledJobHandlerResolver, ITransientDependency
{
    // JobKey → 实现类型。跨作业共享：首次解析建好索引后，再找某个 JobKey 就直接构造那一个处理器，
    // 不必为找一个而构造其余三十多个——既是故障隔离，也把每次作业激活的开销从 O(全部) 降到 O(1)。
    // JobKey 是各处理器的编译期常量属性，映射一旦建立即不会失效。
    private static readonly ConcurrentDictionary<string, Type> JobKeyIndex = new();

    private readonly ScheduledJobHandlerCatalog _catalog;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledJobHandlerResolver> _logger;

    public ScheduledJobHandlerResolver(
        ScheduledJobHandlerCatalog catalog,
        IServiceProvider serviceProvider,
        ILogger<ScheduledJobHandlerResolver> logger)
    {
        _catalog = catalog;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IScheduledJobHandler? Find(string jobKey)
    {
        // 索引命中：只构造目标处理器，全程不触碰其它处理器——别的处理器依赖坏死也影响不到本任务。
        // 目标自己构造失败则返回 null（不再退回全量扫描：索引可信，扫一遍也只会得到同样的失败）。
        if (JobKeyIndex.TryGetValue(jobKey, out var indexedType))
        {
            return TryCreate(indexedType, out var indexed) ? indexed : null;
        }

        foreach (var handlerType in _catalog.HandlerTypes)
        {
            if (!TryCreate(handlerType, out var handler))
            {
                continue;   // 坏的跳过，不牵连本次要找的任务
            }

            JobKeyIndex[handler!.JobKey] = handlerType;
            if (handler.JobKey == jobKey)
            {
                return handler;
            }
        }

        return null;
    }

    public IReadOnlyList<IScheduledJobHandler> ResolveAvailable()
    {
        var available = new List<IScheduledJobHandler>(_catalog.HandlerTypes.Count);

        foreach (var handlerType in _catalog.HandlerTypes)
        {
            if (!TryCreate(handlerType, out var handler))
            {
                continue;
            }

            JobKeyIndex[handler!.JobKey] = handlerType;
            available.Add(handler);
        }

        return available;
    }

    private bool TryCreate(Type handlerType, out IScheduledJobHandler? handler)
    {
        try
        {
            handler = (IScheduledJobHandler)_serviceProvider.GetRequiredService(handlerType);
            return true;
        }
        catch (Exception ex)
        {
            // 单个处理器构造失败不再连坐：记 Warning 供运维定位（异常链里才有真因，如某个外部连接
            // 在构造期就要建立），其余任务继续可用。若坏的正是本次要找的那个，调用方拿到 null，
            // 由其按「未找到 handler」走记历史 + 告警的正常失败路径。
            _logger.LogWarning(
                ex,
                "定时任务处理器 {HandlerType} 构造失败，已跳过；其余任务不受影响",
                handlerType.FullName);
            handler = null;
            return false;
        }
    }
}
