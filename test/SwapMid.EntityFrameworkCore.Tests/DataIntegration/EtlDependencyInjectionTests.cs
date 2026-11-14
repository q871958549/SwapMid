using System.Collections.Generic;
using System.Linq;
using SwapMid.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Xunit;

namespace SwapMid.DataIntegration;

[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class EtlDependencyInjectionTests
    : SwapMidApplicationTestBase<
        SwapMidEntityFrameworkCoreTestModule>
{
    [Fact]
    public void 生产Runner与两类连接工厂_可由真实ABP容器解析()
    {
        // 测试模块会按设计把 IEtlPipelineRunner 替换为 Fake；解析具体生产类型可验证
        // 新增构造依赖与约定注册，同时不破坏应用服务测试的外部库隔离。
        GetRequiredService<EtlPipelineRunner>()
            .ShouldNotBeNull();
        GetRequiredService<IEtlRelationalConnectionFactory>()
            .ShouldBeOfType<EtlRelationalConnectionFactory>();
        GetRequiredService<IDataPipelineExecutionFenceConnectionFactory>()
            .ShouldBeOfType<NpgsqlDataPipelineExecutionFenceConnectionFactory>();
        GetRequiredService<NpgsqlDataPipelineExecutionFence>()
            .ShouldNotBeNull();
        var connectionStringResolvers = GetRequiredService<
                IEnumerable<IConnectionStringResolver>>()
            .ToList();
        connectionStringResolvers.ShouldHaveSingleItem()
            .ShouldBeOfType<PipelinePinnedConnectionStringResolver>();
        GetRequiredService<PipelineFailureNotificationJob>()
            .ShouldNotBeNull();
        var backgroundJob = GetRequiredService<
                IOptions<AbpBackgroundJobOptions>>()
            .Value
            .GetJob(typeof(PipelineFailureNotificationJobArgs));
        backgroundJob.JobType.ShouldBe(
            typeof(PipelineFailureNotificationJob));
    }
}
