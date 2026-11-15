using System;
using System.Text.Json;
using System.Threading.Tasks;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

[BackgroundJobName("SwapMid.DataIntegration.PipelineTerminalEventDispatch:v1")]
public sealed class PipelineTerminalEventJobArgs
{
    public Guid ExecutionId { get; set; }
    public Guid PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public TriggerType TriggerType { get; set; }
    public ExecutionStatus Status { get; set; }
    public long? ProcessedRows { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>
/// 异步将 ETL 终态 Outbox 作业桥接发布到集成事件总线（如 <c>integration.pipeline.events</c>）。
/// </summary>
public sealed class PipelineTerminalEventDispatchJob :
    AsyncBackgroundJob<PipelineTerminalEventJobArgs>,
    ITransientDependency
{
    private readonly IIntegrationEventPublisher _integrationEventPublisher;

    public PipelineTerminalEventDispatchJob(
        IIntegrationEventPublisher integrationEventPublisher)
    {
        _integrationEventPublisher = integrationEventPublisher;
    }

    public override async Task ExecuteAsync(PipelineTerminalEventJobArgs args)
    {
        if (args.ExecutionId == Guid.Empty || args.PipelineId == Guid.Empty)
        {
            return;
        }

        var statusSlug = ToStatusSlug(args.Status);
        var eventId = DeterministicEventId.Create(
            $"swapmid:etl:terminal:{args.ExecutionId:N}:{(int)args.Status}");
        var subject = $"pipeline.execution.{statusSlug}";

        var payloadJson = JsonSerializer.Serialize(new
        {
            executionId = args.ExecutionId,
            pipelineId = args.PipelineId,
            pipelineName = args.PipelineName,
            triggerType = args.TriggerType.ToString(),
            status = args.Status.ToString(),
            processedRows = args.ProcessedRows,
            startedAtUtc = DateTime.SpecifyKind(args.StartedAtUtc, DateTimeKind.Utc),
            completedAtUtc = args.CompletedAtUtc.HasValue
                ? DateTime.SpecifyKind(args.CompletedAtUtc.Value, DateTimeKind.Utc)
                : (DateTime?)null,
            errorCode = args.ErrorCode,
            correlationId = args.CorrelationId,
        });

        await _integrationEventPublisher.PublishIfTopicActiveAsync(
            new IntegrationEventEnvelope(
                eventId,
                IntegrationTopicNames.PipelineEvents,
                subject,
                payloadJson));
    }

    internal static string ToStatusSlug(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Succeeded => "succeeded",
        ExecutionStatus.Failed => "failed",
        ExecutionStatus.Cancelled => "cancelled",
        ExecutionStatus.OutcomeUnknown => "outcome_unknown",
        _ => "unknown",
    };
}
