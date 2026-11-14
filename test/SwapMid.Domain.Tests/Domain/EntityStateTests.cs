using System;
using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.MessageBus;
using Shouldly;
using Xunit;

namespace SwapMid.Domain;

public class EntityStateTests
{
    [Fact]
    public void ApiGroup_Should_Update_And_Toggle_Enabled_State()
    {
        var group = new ApiGroup(Guid.NewGuid(), "Internal APIs", "Initial group", 10);

        group.IsEnabled.ShouldBeTrue();
        group.Name.ShouldBe("Internal APIs");
        group.Description.ShouldBe("Initial group");
        group.Order.ShouldBe(10);

        group.Disable();
        group.Update("Partner APIs", "External partner access", 20);

        group.IsEnabled.ShouldBeFalse();
        group.Name.ShouldBe("Partner APIs");
        group.Description.ShouldBe("External partner access");
        group.Order.ShouldBe(20);

        group.Enable();
        group.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void DataPipeline_Should_Track_Status_And_Last_Execution()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var pipeline = new DataPipeline(
            Guid.NewGuid(),
            "Daily customer sync",
            sourceId,
            targetId,
            "Sync CRM customers",
            "{\"steps\":[]}",
            "0 2 * * *");

        pipeline.Status.ShouldBe(PipelineStatus.Draft);
        pipeline.ScheduleEnabled.ShouldBeFalse();
        pipeline.LastRunAt.ShouldBeNull();
        pipeline.LastRunStatus.ShouldBeNull();

        pipeline.Activate();
        pipeline.Status.ShouldBe(PipelineStatus.Active);

        pipeline.Pause();
        pipeline.Status.ShouldBe(PipelineStatus.Paused);

        var executedAt = new DateTime(2026, 6, 2, 8, 30, 0, DateTimeKind.Utc);
        pipeline.RecordExecution(executedAt, ExecutionStatus.Succeeded);

        pipeline.LastRunAt.ShouldBe(executedAt);
        pipeline.LastRunStatus.ShouldBe(ExecutionStatus.Succeeded);

        pipeline.Archive();
        pipeline.Status.ShouldBe(PipelineStatus.Archived);
    }

    [Fact]
    public void MessageRecord_Should_Move_Through_Delivery_And_Retry_States()
    {
        var record = new MessageRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "orders.created",
            "order.created",
            "{\"id\":\"order-1\"}");

        record.Status.ShouldBe(MessageStatus.Pending);
        record.RetryCount.ShouldBe(0);
        record.ErrorMessage.ShouldBeNull();

        record.MarkFailed("broker timeout");

        record.Status.ShouldBe(MessageStatus.Failed);
        record.RetryCount.ShouldBe(1);
        record.ErrorMessage.ShouldBe("broker timeout");

        record.ResetForRetry();

        record.Status.ShouldBe(MessageStatus.Pending);
        record.RetryCount.ShouldBe(1);
        record.ErrorMessage.ShouldBeNull();

        record.MarkDelivered();

        record.Status.ShouldBe(MessageStatus.Delivered);
        record.DeliveredAt.ShouldNotBeNull();
    }
}
