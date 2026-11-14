using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Volo.Abp.DependencyInjection;

namespace SwapMid.HealthChecks;

// Redis 探活:经应用已注入的 IDistributedCache(StackExchange.Redis 后端）写读一个短 TTL 探针键。
// 复用平台分布式缓存连接，无需额外引第三方探针包（避免与 StackExchange.Redis 版本耦合）。
// Redis 不可用时：ABP 缓存击穿尚能回源 DB，但前端会话（web 容器 db1）会丢 → 全员被踢下线，
// 属用户立刻有感的故障，故纳入 /health-status 主动暴露。
public class SwapMidRedisCheck : IHealthCheck, ITransientDependency
{
    private const string ProbeKey = "swapmid:health:redis-probe";

    private readonly IDistributedCache _cache;

    public SwapMidRedisCheck(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var stamp = Guid.NewGuid().ToString("N");
            await _cache.SetStringAsync(
                ProbeKey,
                stamp,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) },
                cancellationToken);

            var read = await _cache.GetStringAsync(ProbeKey, cancellationToken);

            return read == stamp
                ? HealthCheckResult.Healthy("Redis read/write ok.")
                : HealthCheckResult.Degraded("Redis reachable but probe value mismatch.");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("Redis read/write failed.", e);
        }
    }
}
