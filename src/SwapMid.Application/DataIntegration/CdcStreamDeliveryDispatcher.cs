using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NpgsqlTypes;
using SwapMid.Monitoring;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 统一将 PostgreSQL CDC 捕获的 WAL 变更与初始全量快照区块分发到
/// <see cref="CdcTargetMode.MessageBusTopic"/>、<see cref="CdcTargetMode.DataPipeline"/> 或 <see cref="CdcTargetMode.ExternalSink"/>。
/// </summary>
internal static class CdcStreamDeliveryDispatcher
{
    internal static async Task<long> DeliverAsync(
        CdcStreamDefinition stream,
        DataSource? targetSink,
        IReadOnlyList<PgOutputCapturedChange> changes,
        NpgsqlLogSequenceNumber? batchEndLsn,
        IIntegrationEventPublisher integrationEventPublisher,
        DataPipelineExecutionCoordinator executionCoordinator,
        IReadOnlyDictionary<DataSourceType, IEtlSinkWriter> sinkWriters,
        CancellationToken cancellationToken,
        long eventSequenceOffset = 0,
        string? idempotencyKeyOverride = null,
        bool triggerPipeline = true)
    {
        if (stream.TargetMode == CdcTargetMode.MessageBusTopic)
        {
            var topicName = string.IsNullOrWhiteSpace(stream.TargetTopicName)
                ? IntegrationTopicNames.CdcEvents
                : stream.TargetTopicName.Trim();

            long published = 0;
            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                var seq = (int)((eventSequenceOffset + i) & 0x7FFF_FFFF);
                var eventId = CdcProtocolHelpers.BuildDeterministicEventId(
                    stream.Id,
                    change.Lsn,
                    seq,
                    change.Operation,
                    change.Schema,
                    change.Table);
                var subject = CdcProtocolHelpers.BuildEventSubject(change.Schema, change.Table, change.Operation);
                var payloadJson = CdcProtocolHelpers.BuildEventPayloadJson(
                    stream.Id,
                    stream.Name,
                    change.Schema,
                    change.Table,
                    change.Operation,
                    change.Lsn,
                    change.CommitTimestampUtc,
                    change.Data,
                    change.OldData);

                var accepted = await integrationEventPublisher.PublishIfTopicActiveAsync(
                    new IntegrationEventEnvelope(eventId, topicName, subject, payloadJson),
                    cancellationToken);
                if (accepted)
                {
                    published++;
                }
            }

            PlatformMetrics.CdcDeliveriesTotal.WithLabels("message_bus_topic").Inc();
            return published;
        }

        if (stream.TargetMode == CdcTargetMode.DataPipeline)
        {
            if (!triggerPipeline) return 0;
            if (!stream.TargetPipelineId.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "DataPipeline 目标模式下必须选择关联的数据管道。");
            }

            var endLsnText = idempotencyKeyOverride ?? batchEndLsn?.ToString() ?? changes[^1].Lsn;
            var runId = CdcProtocolHelpers.BuildDeterministicPipelineRunId(stream.Id, endLsnText);
            await executionCoordinator.ExecuteAsync(
                stream.TargetPipelineId.Value,
                runId,
                TriggerType.Scheduled,
                cancellationToken);
            PlatformMetrics.CdcDeliveriesTotal.WithLabels("data_pipeline").Inc();
            return 1;
        }

        if (stream.TargetMode == CdcTargetMode.ExternalSink)
        {
            if (targetSink == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "ExternalSink 目标模式下未找到目标外部接收端数据源。");
            }

            var written = await CdcExternalSinkDeliveryAdapter.DeliverAsync(
                stream,
                targetSink,
                changes,
                batchEndLsn,
                sinkWriters,
                cancellationToken,
                eventSequenceOffset,
                idempotencyKeyOverride);
            var label = targetSink.Type switch
            {
                DataSourceType.Kafka => "kafka",
                DataSourceType.ClickHouse => "clickhouse",
                DataSourceType.ObjectStorage => "object_storage",
                _ => "external_sink",
            };
            PlatformMetrics.CdcDeliveriesTotal.WithLabels(label).Inc();
            return written;
        }

        return 0;
    }
}
