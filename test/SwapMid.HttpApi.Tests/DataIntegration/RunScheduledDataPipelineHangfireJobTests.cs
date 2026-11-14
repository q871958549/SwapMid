using SwapMid.HttpApiHost.DataIntegration;
using Shouldly;

namespace SwapMid.HttpApi.Tests.DataIntegration;

public class RunScheduledDataPipelineHangfireJobTests
{
    [Fact]
    public void CreateMinuteDeterministicRunId_Should_Be_Deterministic_Within_Same_Utc_Minute_And_Vary_Across_Minutes()
    {
        var pipelineId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var t1 = new DateTime(2026, 5, 1, 10, 15, 02, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 5, 1, 10, 15, 58, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 5, 1, 10, 16, 00, DateTimeKind.Utc);

        var run1 = RunScheduledDataPipelineHangfireJob.CreateMinuteDeterministicRunId(pipelineId, t1);
        var run2 = RunScheduledDataPipelineHangfireJob.CreateMinuteDeterministicRunId(pipelineId, t2);
        var run3 = RunScheduledDataPipelineHangfireJob.CreateMinuteDeterministicRunId(pipelineId, t3);

        run1.ShouldNotBe(Guid.Empty);
        run1.ShouldBe(run2);
        run1.ShouldNotBe(run3);
    }
}
