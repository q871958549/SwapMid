using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace SwapMid.ApiGateway;

public readonly record struct GatewayRateLimitLease(bool Allowed, int RetryAfterSeconds);

public interface IGatewayRateLimiter
{
    GatewayRateLimitLease TryAcquire(string partitionKey, int permitLimitPerMinute);
}

/// <summary>
/// 支持按客户端动态配额（<c>RateLimitPerMinute</c>）的 60 秒滑动窗口限流器，带桶容量上界防高基数内存膨胀。
/// </summary>
public sealed class GatewaySlidingWindowRateLimiter : IGatewayRateLimiter
{
    private const int MaxTrackedPartitions = 4_096;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private int _cleanupTick;

    public GatewaySlidingWindowRateLimiter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public GatewayRateLimitLease TryAcquire(string partitionKey, int permitLimitPerMinute)
    {
        var limit = Math.Clamp(permitLimitPerMinute, 1, 60_000);
        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        if (_buckets.Count > MaxTrackedPartitions &&
            (Interlocked.Increment(ref _cleanupTick) & 0x3F) == 0)
        {
            EvictStaleBuckets(nowTicks);
        }

        var bucket = _buckets.GetOrAdd(partitionKey, static _ => new Bucket());
        return bucket.TryAcquire(nowTicks, Window.Ticks, limit);
    }

    private void EvictStaleBuckets(long nowTicks)
    {
        var cutoff = nowTicks - Window.Ticks;
        foreach (var pair in _buckets)
        {
            if (pair.Value.IsStale(cutoff))
            {
                _buckets.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed class Bucket
    {
        private readonly Queue<long> _timestamps = new();
        private long _lastSeenTicks;

        public GatewayRateLimitLease TryAcquire(long nowTicks, long windowTicks, int limit)
        {
            lock (_timestamps)
            {
                _lastSeenTicks = nowTicks;
                var cutoff = nowTicks - windowTicks;
                while (_timestamps.Count > 0 && _timestamps.Peek() <= cutoff)
                {
                    _timestamps.Dequeue();
                }

                if (_timestamps.Count >= limit)
                {
                    var oldest = _timestamps.Peek();
                    var remainingTicks = Math.Max(TimeSpan.TicksPerSecond, (oldest + windowTicks) - nowTicks);
                    var retryAfterSeconds = (int)Math.Ceiling((double)remainingTicks / TimeSpan.TicksPerSecond);
                    return new GatewayRateLimitLease(false, Math.Max(1, retryAfterSeconds));
                }

                _timestamps.Enqueue(nowTicks);
                return new GatewayRateLimitLease(true, 0);
            }
        }

        public bool IsStale(long cutoffTicks)
        {
            lock (_timestamps)
            {
                return _lastSeenTicks <= cutoffTicks;
            }
        }
    }
}
