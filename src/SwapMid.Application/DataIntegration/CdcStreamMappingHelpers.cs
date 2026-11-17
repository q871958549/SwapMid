using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.DataIntegration;

internal sealed record NormalizedCdcStreamInput(
    string Name,
    string PublicationName,
    string SlotName,
    string? MonitoredTables,
    string? TargetTopicName,
    Guid? TargetPipelineId,
    Guid? TargetSinkId,
    string? TargetSinkDestination,
    string? TargetSinkKeyColumns);

/// <summary>
/// <see cref="CdcStreamAppService"/> 的输入规范化与 DTO 映射辅助类。
/// </summary>
internal static class CdcStreamMappingHelpers
{
    internal static async Task<NormalizedCdcStreamInput> ValidateAndNormalizeInputAsync(
        CdcStreamCreateUpdateDto input,
        Guid? excludeId,
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        IRepository<DataPipeline, Guid> pipelineRepository)
    {
        var name = input.Name.Trim();
        if (await streamRepository.NameExistsAsync(name, excludeId))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamNameAlreadyExists);
        }

        var source = await sourceRepository.FindAsync(input.SourceId);
        if (source == null)
        {
            throw new EntityNotFoundException(typeof(DataSource), input.SourceId);
        }

        PgOutputCdcCaptureService.EnsurePostgreSqlSource(source);

        var pub = CdcProtocolHelpers.ValidateReplicationIdentifier(input.PublicationName, "PublicationName");
        var slot = CdcProtocolHelpers.ValidateReplicationIdentifier(input.SlotName, "SlotName");
        var parsedTables = CdcProtocolHelpers.ParseMonitoredTables(input.MonitoredTables);
        var normalizedTables = parsedTables.Count > 0
            ? string.Join(", ", parsedTables.Select(t => t.FullName))
            : null;

        string? topicName = null;
        Guid? pipelineId = null;
        Guid? sinkId = null;
        string? sinkDestination = null;
        string? sinkKeyColumns = null;

        if (input.TargetMode == CdcTargetMode.MessageBusTopic)
        {
            topicName = string.IsNullOrWhiteSpace(input.TargetTopicName)
                ? IntegrationTopicNames.CdcEvents
                : input.TargetTopicName.Trim();
        }
        else if (input.TargetMode == CdcTargetMode.DataPipeline)
        {
            if (!input.TargetPipelineId.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "联动管道模式下必须选择关联的目标数据管道。");
            }

            var pipeline = await pipelineRepository.FindAsync(input.TargetPipelineId.Value);
            if (pipeline == null)
            {
                throw new EntityNotFoundException(typeof(DataPipeline), input.TargetPipelineId.Value);
            }

            pipelineId = pipeline.Id;
        }
        else if (input.TargetMode == CdcTargetMode.ExternalSink)
        {
            if (!input.TargetSinkId.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "外部接收端模式下必须选择目标接收端数据源。");
            }

            var sink = await sourceRepository.FindAsync(input.TargetSinkId.Value);
            if (sink == null)
            {
                throw new EntityNotFoundException(typeof(DataSource), input.TargetSinkId.Value);
            }

            if (sink.Type is not (DataSourceType.Kafka or DataSourceType.ClickHouse or DataSourceType.ObjectStorage))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"目标数据源仅支持 Kafka / ClickHouse / ObjectStorage，当前为 {sink.Type}。");
            }

            var dest = input.TargetSinkDestination?.Trim();
            if (string.IsNullOrWhiteSpace(dest) || !EtlTransformConfig.IsValidDestinationFor(sink.Type, dest))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "外部接收端目标标识（Topic / 库表 / 对象前缀）缺失或格式不合法。");
            }

            var keyCols = CdcProtocolHelpers.ParseSinkKeyColumns(input.TargetSinkKeyColumns);
            sinkId = sink.Id;
            sinkDestination = dest;
            sinkKeyColumns = keyCols.Count > 0 ? string.Join(", ", keyCols) : null;
        }
        else if (input.TargetMode == CdcTargetMode.RelationalTable)
        {
            if (parsedTables.Count != 1)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "关系表行级镜像模式下必须在监听表清单中指定唯一 1 张上游源表（示例：public.orders）。");
            }

            if (!input.TargetSinkId.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "关系表行级镜像模式下必须选择目标关系型数据库数据源。");
            }

            var targetDb = await sourceRepository.FindAsync(input.TargetSinkId.Value);
            if (targetDb == null)
            {
                throw new EntityNotFoundException(typeof(DataSource), input.TargetSinkId.Value);
            }

            if (targetDb.Type != DataSourceType.RelationalDatabase)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"关系表行级镜像目标必须为关系型数据库，当前为 {targetDb.Type}。");
            }

            var dest = input.TargetSinkDestination?.Trim();
            if (string.IsNullOrWhiteSpace(dest) ||
                !EtlTransformConfig.IsValidDestinationFor(DataSourceType.RelationalDatabase, dest))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "关系型目标表名缺失或格式不合法（示例：public.orders_mirror）。");
            }

            var keyCols = CdcProtocolHelpers.ParseSinkKeyColumns(input.TargetSinkKeyColumns);
            if (keyCols.Count == 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "关系表行级镜像模式下必须填写至少一个业务主键列（TargetSinkKeyColumns）。");
            }

            sinkId = targetDb.Id;
            sinkDestination = dest;
            sinkKeyColumns = string.Join(", ", keyCols);
        }
        else
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "不支持的 CDC 下游投递模式。");
        }

        return new NormalizedCdcStreamInput(
            name,
            pub,
            slot,
            normalizedTables,
            topicName,
            pipelineId,
            sinkId,
            sinkDestination,
            sinkKeyColumns);
    }

    internal static CdcStreamDto MapToDto(
        CdcStreamDefinition stream,
        string? sourceName,
        string? targetPipelineName,
        DataSource? targetSink) => new()
    {
        Id = stream.Id,
        CreationTime = stream.CreationTime,
        CreatorId = stream.CreatorId,
        LastModificationTime = stream.LastModificationTime,
        LastModifierId = stream.LastModifierId,
        IsDeleted = stream.IsDeleted,
        DeleterId = stream.DeleterId,
        DeletionTime = stream.DeletionTime,
        Name = stream.Name,
        Description = stream.Description,
        SourceId = stream.SourceId,
        SourceName = sourceName,
        PublicationName = stream.PublicationName,
        SlotName = stream.SlotName,
        MonitoredTables = stream.MonitoredTables,
        AutoCreatePublicationAndSlot = stream.AutoCreatePublicationAndSlot,
        PollIntervalSeconds = stream.PollIntervalSeconds,
        MaxBatchSize = stream.MaxBatchSize,
        TargetMode = stream.TargetMode,
        TargetTopicName = stream.TargetTopicName,
        TargetPipelineId = stream.TargetPipelineId,
        TargetPipelineName = targetPipelineName,
        TargetSinkId = stream.TargetSinkId,
        TargetSinkName = targetSink?.Name,
        TargetSinkType = targetSink?.Type,
        TargetSinkDestination = stream.TargetSinkDestination,
        TargetSinkKeyColumns = stream.TargetSinkKeyColumns,
        TargetDeletePolicy = stream.TargetDeletePolicy,
        SchemaDriftPolicy = stream.SchemaDriftPolicy,
        SchemaBaselineHash = stream.SchemaBaselineHash,
        SchemaDriftDetected = stream.SchemaDriftDetected,
        SchemaDriftSummary = stream.SchemaDriftSummary,
        SchemaDriftDetectedAt = stream.SchemaDriftDetectedAt,
        InitialSnapshotEnabled = stream.InitialSnapshotEnabled,
        SnapshotStatus = stream.SnapshotStatus,
        SnapshotCompletedAt = stream.SnapshotCompletedAt,
        SnapshotRowCount = stream.SnapshotRowCount,
        Status = stream.Status,
        CheckpointLsn = stream.CheckpointLsn,
        LastCapturedAt = stream.LastCapturedAt,
        LastCapturedCount = stream.LastCapturedCount,
        TotalCapturedCount = stream.TotalCapturedCount,
        LastErrorCode = stream.LastErrorCode,
        LastErrorMessage = stream.LastErrorMessage,
    };
}
