using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SwapMid.Notification;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.DataIntegration;

internal sealed record NormalizedCdcStreamInput(
    string Name,
    CdcSourceType SourceType,
    string PublicationName,
    string SlotName,
    string? MonitoredTables,
    string? TargetTopicName,
    Guid? TargetPipelineId,
    Guid? TargetSinkId,
    string? TargetSinkDestination,
    string? TargetSinkKeyColumns,
    string? TargetFieldMappingsJson,
    string? SoftDeleteColumn,
    string? SoftDeleteTimestampColumn,
    int? LakehouseCompactMinFiles,
    LakehouseTableFormat LakehouseTableFormat);

/// <summary>
/// <see cref="CdcStreamAppService"/> 的输入规范化与 DTO 映射辅助类。
/// </summary>
internal static partial class CdcStreamMappingHelpers
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

        CdcStreamMappingHelpers.EnsureSourceMatchesType(input.SourceType, source);

        var defaultSchema = input.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => "dbo",
            CdcSourceType.MySqlBinlog => RelationalDataSourceConnection.Parse(source.Config).Database?.Trim() ?? string.Empty,
            _ => "public",
        };
        var parsedTables = CdcProtocolHelpers.ParseMonitoredTables(input.MonitoredTables, defaultSchema: defaultSchema);
        if (input.SourceType == CdcSourceType.SqlServerChangeTracking && parsedTables.Count == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "SQL Server Change Tracking 模式下必须指定至少 1 张监听表（示例：dbo.orders）。");
        }

        if (input.SourceType == CdcSourceType.MySqlBinlog &&
            (parsedTables.Count == 0 || parsedTables.Any(t => string.IsNullOrWhiteSpace(t.Schema))))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "MySQL Binlog 模式下必须指定含库名的监听表（如 appdb.orders 或在数据源中配置 database）。");
        }

        var pub = input.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => BuildIdentifierFallback(input.PublicationName, "sqlserver_ct"),
            CdcSourceType.MySqlBinlog => BuildIdentifierFallback(input.PublicationName, "mysql_binlog"),
            _ => CdcProtocolHelpers.ValidateReplicationIdentifier(input.PublicationName, "PublicationName"),
        };
        var slot = input.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => BuildIdentifierFallback(input.SlotName, $"ct_{name}"),
            CdcSourceType.MySqlBinlog => BuildIdentifierFallback(input.SlotName, $"binlog_{name}"),
            _ => CdcProtocolHelpers.ValidateReplicationIdentifier(input.SlotName, "SlotName"),
        };
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

        string? targetFieldMappingsJson = null;
        string? softDeleteColumn = null;
        string? softDeleteTimestampColumn = null;
        if (input.TargetMode == CdcTargetMode.RelationalTable)
        {
            if (input.TargetFieldMappings is { Count: > 0 })
            {
                var rawMappingsJson = JsonSerializer.Serialize(input.TargetFieldMappings);
                targetFieldMappingsJson = CdcStreamMirrorConfigValidator.NormalizeFieldMappingsJson(rawMappingsJson);
            }

            softDeleteColumn = CdcStreamMirrorConfigValidator.NormalizeIdentifier(
                input.SoftDeleteColumn,
                CdcStreamConsts.MaxSoftDeleteColumnLength,
                nameof(input.SoftDeleteColumn));
            softDeleteTimestampColumn = CdcStreamMirrorConfigValidator.NormalizeIdentifier(
                input.SoftDeleteTimestampColumn,
                CdcStreamConsts.MaxSoftDeleteTimestampColumnLength,
                nameof(input.SoftDeleteTimestampColumn));

            CdcStreamMirrorConfigValidator.ValidateMirrorColumns(
                input.TargetMode,
                sinkKeyColumns,
                targetFieldMappingsJson,
                input.TargetDeletePolicy,
                softDeleteColumn,
                softDeleteTimestampColumn);
        }

        int? lakehouseCompactMinFiles = null;
        var lakehouseTableFormat = LakehouseTableFormat.SwapMidManifest;
        if (input.TargetMode == CdcTargetMode.ExternalSink && input.TargetSinkId.HasValue)
        {
            var externalSink = await sourceRepository.FindAsync(input.TargetSinkId.Value);
            if (input.LakehouseCompactMinFiles is > 0)
            {
                if (externalSink?.Type != DataSourceType.ObjectStorage)
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", "仅 ObjectStorage 接收端支持设置 Lakehouse 自动压缩分片阈值（LakehouseCompactMinFiles）。");
                }

                lakehouseCompactMinFiles = CdcStreamMirrorConfigValidator.NormalizeLakehouseCompactMinFiles(
                    input.TargetMode,
                    input.LakehouseCompactMinFiles);
            }

            if (input.LakehouseTableFormat == LakehouseTableFormat.DeltaLake)
            {
                if (externalSink?.Type != DataSourceType.ObjectStorage)
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", "仅 ObjectStorage 接收端支持开启 Apache Delta Lake（_delta_log）表协议。");
                }

                var storageCfg = ObjectStorageSinkConfigContract.ParseForWrite(externalSink.Config, requireSecretKey: false);
                if (!string.Equals(storageCfg.Format, "parquet", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", "Delta Lake（_delta_log）表协议要求 ObjectStorage 接收端采用 Parquet 格式。");
                }

                lakehouseTableFormat = LakehouseTableFormat.DeltaLake;
            }
        }

        return new NormalizedCdcStreamInput(
            name,
            input.SourceType,
            pub,
            slot,
            normalizedTables,
            topicName,
            pipelineId,
            sinkId,
            sinkDestination,
            sinkKeyColumns,
            targetFieldMappingsJson,
            softDeleteColumn,
            softDeleteTimestampColumn,
            lakehouseCompactMinFiles,
            lakehouseTableFormat);
    }

    internal static async Task<CdcPreflightResultDto> BuildPreflightWithMirrorCheckAsync(
        PgOutputCdcCaptureService captureService,
        SqlServerCdcCaptureService? sqlServerCaptureService,
        MySqlBinlogCdcCaptureService? mySqlCaptureService,
        CdcRelationalMirrorPreflightService? mirrorPreflightService,
        IRepository<DataSource, Guid> sourceRepository,
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        var preflight = await DispatchPreflightAsync(
            captureService, sqlServerCaptureService, mySqlCaptureService, source, stream, cancellationToken);
        if (stream.TargetMode != CdcTargetMode.RelationalTable ||
            !stream.TargetSinkId.HasValue ||
            mirrorPreflightService == null)
        {
            return preflight;
        }

        var targetSink = await sourceRepository.FindAsync(
            stream.TargetSinkId.Value,
            includeDetails: false,
            cancellationToken: cancellationToken);
        if (targetSink == null)
        {
            preflight.IsReady = false;
            preflight.Diagnostics.Add($"目标关系数据源（{stream.TargetSinkId.Value}）不存在。");
            return preflight;
        }

        var compatibility = await mirrorPreflightService.EvaluateStreamAsync(
            source,
            targetSink,
            stream,
            cancellationToken);
        preflight.MirrorCompatibility = compatibility;
        if (!compatibility.Compatible)
        {
            preflight.IsReady = false;
        }

        foreach (var issue in compatibility.Issues)
        {
            var prefix = string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)
                ? "[镜像兼容错误]"
                : "[镜像兼容告警]";
            preflight.Diagnostics.Add($"{prefix} {issue.Message}");
        }

        return preflight;
    }

    internal static async Task<(CdcStreamDefinition Stream, DataSource Sink)> LoadExternalSinkContextAsync(
        Guid streamId,
        string reasonMessage,
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        CancellationToken cancellationToken)
    {
        var stream = await streamRepository.GetAsync(streamId, cancellationToken: cancellationToken);
        if (stream.TargetMode != CdcTargetMode.ExternalSink || !stream.TargetSinkId.HasValue)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", reasonMessage);
        }

        var sink = await sourceRepository.GetAsync(stream.TargetSinkId.Value, cancellationToken: cancellationToken);
        return (stream, sink);
    }

    internal static async Task NotifySchemaAcceptedAsync(
        CdcStreamDefinition stream,
        string baselineHash,
        INotificationService? notificationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (notificationService == null)
        {
            return;
        }

        try
        {
            await notificationService.SendAsync(
                new NotificationSendRequest
                {
                    Title = $"CDC 流「{stream.Name}」Schema 漂移已人工确认恢复",
                    Content = $"已将最新 Schema 指纹（{baselineHash[..12]}）记录为新基线，可在控制台恢复或激活捕获。",
                    Level = NotificationLevel.Info,
                    Source = NotificationSources.DataIntegration,
                    ActionPath = "/data-integration/cdc",
                    ActionLabel = "查看 CDC 流",
                    RecoveryOfTitle = $"CDC 流「{stream.Name}」上游 Schema 漂移已自动暂停",
                },
                cancellationToken);
        }
        catch (Exception notifyEx)
        {
            logger.LogWarning(
                "CDC Schema 确认恢复通知发送失败 streamId={StreamId} exceptionType={Type}",
                stream.Id,
                notifyEx.GetType().FullName ?? notifyEx.GetType().Name);
        }
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
        SourceType = stream.SourceType,
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
        TargetFieldMappings = CdcStreamMirrorConfigValidator.ParseFieldMappings(stream.TargetFieldMappingsJson)
            .Select(m => new CdcFieldMappingDto
            {
                SourceColumn = m.SourceColumn,
                TargetColumn = m.TargetColumn,
            })
            .ToList(),
        SoftDeleteColumn = stream.SoftDeleteColumn,
        SoftDeleteTimestampColumn = stream.SoftDeleteTimestampColumn,
        LakehouseCompactMinFiles = stream.LakehouseCompactMinFiles,
        LakehouseTableFormat = stream.LakehouseTableFormat,
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

    private static string BuildIdentifierFallback(string? raw, string defaultValue)
    {
        var candidate = !string.IsNullOrWhiteSpace(raw) ? raw.Trim() : defaultValue;
        return candidate.Length <= CdcStreamConsts.MaxPostgresIdentifierLength
            ? candidate
            : candidate[..CdcStreamConsts.MaxPostgresIdentifierLength];
    }
}
