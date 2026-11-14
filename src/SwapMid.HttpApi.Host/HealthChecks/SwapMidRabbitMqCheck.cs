using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.RabbitMQ;

namespace SwapMid.HealthChecks;

// RabbitMQ 探活:从 ABP IChannelPool 取一个信道再释放，验证 broker 连接可用。
// 复用消息总线投递（RabbitMqMessageDeliveryClient）已在用的同一连接池，零额外依赖，
// 也就避开了第三方 RabbitMQ 探针包与 RabbitMQ.Client 7 的 API/版本冲突。
public class SwapMidRabbitMqCheck : IHealthCheck, ITransientDependency
{
    private readonly IChannelPool _channelPool;

    public SwapMidRabbitMqCheck(IChannelPool channelPool)
    {
        _channelPool = channelPool;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var accessor = await _channelPool.AcquireAsync();

            return accessor.Channel.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ channel acquired.")
                : HealthCheckResult.Unhealthy("RabbitMQ channel acquired but not open.");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ channel acquire failed.", e);
        }
    }
}
