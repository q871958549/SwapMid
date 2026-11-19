using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace SwapMid.DataIntegration;

internal static partial class CdcStreamMappingHelpers
{
    internal static void EnsureSourceMatchesType(CdcSourceType sourceType, DataSource source)
    {
        switch (sourceType)
        {
            case CdcSourceType.SqlServerChangeTracking:
                SqlServerCdcCaptureService.EnsureSqlServerSource(source);
                break;
            case CdcSourceType.MySqlBinlog:
                MySqlBinlogCdcCaptureService.EnsureMySqlSource(source);
                break;
            case CdcSourceType.PushWebhook:
                CdcWebhookIngressService.EnsurePushWebhookSource(source);
                break;
            case CdcSourceType.KafkaDebezium:
                KafkaDebeziumCdcCaptureService.EnsureKafkaSource(source);
                break;
            default:
                PgOutputCdcCaptureService.EnsurePostgreSqlSource(source);
                break;
        }
    }

    internal static Task<CdcSchemaDriftEvaluation> EvaluateSchemaForStreamAsync(
        PgOutputCdcCaptureService pgCaptureService,
        SqlServerCdcCaptureService? sqlServerCaptureService,
        MySqlBinlogCdcCaptureService? mySqlCaptureService,
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        return stream.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => (sqlServerCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "SQL Server Change Tracking 捕获引擎未启用。"))
                .EvaluateSchemaAsync(source, stream, cancellationToken),
            CdcSourceType.MySqlBinlog => (mySqlCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "MySQL Binlog 捕获引擎未启用。"))
                .EvaluateSchemaAsync(source, stream, cancellationToken),
            CdcSourceType.PushWebhook or CdcSourceType.KafkaDebezium => Task.FromResult(
                CdcSchemaDriftEvaluator.Compare(stream.SchemaBaselineJson, Array.Empty<CdcCapturedTableSchema>())),
            _ => pgCaptureService.EvaluateSchemaAsync(source, stream, cancellationToken),
        };
    }

    internal static async Task<CdcPreflightResultDto> DispatchPreflightAsync(
        PgOutputCdcCaptureService pgCaptureService,
        SqlServerCdcCaptureService? sqlServerCaptureService,
        MySqlBinlogCdcCaptureService? mySqlCaptureService,
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken,
        KafkaDebeziumCdcCaptureService? kafkaCaptureService = null)
    {
        return stream.SourceType switch
        {
            CdcSourceType.SqlServerChangeTracking => await (sqlServerCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "SQL Server Change Tracking 捕获引擎未启用。"))
                .PreflightAsync(source, stream, cancellationToken),
            CdcSourceType.MySqlBinlog => await (mySqlCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "MySQL Binlog 捕获引擎未启用。"))
                .PreflightAsync(source, stream, cancellationToken),
            CdcSourceType.PushWebhook => CdcWebhookIngressService.BuildPreflight(source, stream),
            CdcSourceType.KafkaDebezium => await (kafkaCaptureService
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "Kafka Debezium CDC 捕获引擎未启用。"))
                .PreflightAsync(source, stream, cancellationToken),
            _ => await pgCaptureService.PreflightAsync(source, stream, cancellationToken),
        };
    }

    internal static string? NormalizeCheckpointResetToken(CdcSourceType sourceType, string? rawCheckpoint)
    {
        if (string.IsNullOrWhiteSpace(rawCheckpoint))
        {
            return null;
        }

        if (sourceType == CdcSourceType.SqlServerChangeTracking)
        {
            if (!long.TryParse(rawCheckpoint.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version < 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "SQL Server Change Tracking 位点必须是非负整数版本号。");
            }

            return version.ToString(CultureInfo.InvariantCulture);
        }

        if (sourceType == CdcSourceType.MySqlBinlog)
        {
            return MySqlBinlogCdcCaptureService.ValidateCheckpoint(rawCheckpoint).Token;
        }

        if (sourceType == CdcSourceType.PushWebhook)
        {
            return CdcWebhookIngressService.NormalizeWebhookCheckpoint(rawCheckpoint);
        }

        if (sourceType == CdcSourceType.KafkaDebezium)
        {
            return KafkaDebeziumEnvelopeParser.NormalizeCheckpointToken(rawCheckpoint);
        }

        return CdcProtocolHelpers.ValidateLsn(rawCheckpoint).ToString();
    }

    internal static async Task<List<CdcStreamDto>> MapToDtoListAsync(
        IReadOnlyList<CdcStreamDefinition> items,
        IRepository<DataSource, Guid> sourceRepository,
        IRepository<DataPipeline, Guid> pipelineRepository)
    {
        var sourceIds = items.SelectMany(x =>
            {
                var ids = new List<Guid>(3) { x.SourceId };
                if (x.TargetSinkId.HasValue) ids.Add(x.TargetSinkId.Value);
                if (x.CatalogSinkId.HasValue) ids.Add(x.CatalogSinkId.Value);
                return ids;
            })
            .Distinct()
            .ToList();
        var sources = sourceIds.Count > 0
            ? (await sourceRepository.GetListAsync(x => sourceIds.Contains(x.Id))).ToDictionary(x => x.Id)
            : new Dictionary<Guid, DataSource>();

        var pipelineIds = items.Where(x => x.TargetPipelineId.HasValue).Select(x => x.TargetPipelineId!.Value).Distinct().ToList();
        var pipelines = pipelineIds.Count > 0
            ? (await pipelineRepository.GetListAsync(x => pipelineIds.Contains(x.Id))).ToDictionary(x => x.Id, x => x.Name)
            : new Dictionary<Guid, string>();

        return items.Select(x =>
        {
            var src = sources.GetValueOrDefault(x.SourceId);
            var sink = x.TargetSinkId.HasValue ? sources.GetValueOrDefault(x.TargetSinkId.Value) : null;
            var catalogSink = x.CatalogSinkId.HasValue ? sources.GetValueOrDefault(x.CatalogSinkId.Value) : null;
            var pipe = x.TargetPipelineId.HasValue ? pipelines.GetValueOrDefault(x.TargetPipelineId.Value) : null;
            return MapToDto(x, src?.Name, pipe, sink, catalogSink);
        }).ToList();
    }

    internal static CdcStreamDto MapToDto(
        CdcStreamDefinition stream,
        string? sourceName,
        string? targetPipelineName,
        DataSource? targetSink,
        DataSource? catalogSink = null)
    {
        var contractEval = CdcDataContractEvaluator.EvaluateSnapshot(stream, DateTime.UtcNow);
        var effectiveViolated = contractEval.Violated || (stream.HasDataContractConfigured && stream.ContractViolated);
        var effectiveStatus = effectiveViolated
            ? CdcContractStatus.Breached
            : contractEval.Status;
        var effectiveSummary = contractEval.Violated
            ? contractEval.Summary
            : stream.ContractViolated
                ? stream.ContractViolationSummary
                : null;

        return new CdcStreamDto
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
            LakehousePartitionStrategy = stream.LakehousePartitionStrategy,
            LakehouseWriteMode = stream.LakehouseWriteMode,
            CatalogType = stream.CatalogType,
            CatalogSinkId = stream.CatalogSinkId,
            CatalogSinkName = catalogSink?.Name,
            CatalogNamespace = stream.CatalogNamespace,
            CatalogTableName = stream.CatalogTableName,
            CatalogLastSyncedVersion = stream.CatalogLastSyncedVersion,
            CatalogLastSyncedAt = stream.CatalogLastSyncedAt,
            CatalogLastSyncStatus = stream.CatalogLastSyncStatus,
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
            MaxFreshnessLagSeconds = stream.MaxFreshnessLagSeconds,
            MaxAllowedWalLagBytes = stream.MaxAllowedWalLagBytes,
            RequiredColumns = stream.RequiredColumns,
            ContractViolated = effectiveViolated,
            ContractViolationSummary = effectiveSummary,
            ContractLastEvaluatedAt = stream.ContractLastEvaluatedAt,
            FreshnessLagSeconds = contractEval.FreshnessLagSeconds,
            ContractStatus = effectiveStatus,
        };
    }

    internal static async Task<CdcStreamDto> EnrichDtoAsync(
        CdcStreamDefinition stream,
        IRepository<DataSource, Guid> sourceRepository,
        IRepository<DataPipeline, Guid> pipelineRepository)
    {
        var source = await sourceRepository.FindAsync(stream.SourceId);
        var pipeline = stream.TargetPipelineId.HasValue
            ? await pipelineRepository.FindAsync(stream.TargetPipelineId.Value)
            : null;
        var sink = stream.TargetSinkId.HasValue
            ? await sourceRepository.FindAsync(stream.TargetSinkId.Value)
            : null;
        var catalogSink = stream.CatalogSinkId.HasValue
            ? await sourceRepository.FindAsync(stream.CatalogSinkId.Value)
            : null;
        return MapToDto(stream, source?.Name, pipeline?.Name, sink, catalogSink);
    }

    internal static readonly IReadOnlyDictionary<string, string> StreamSortingWhitelist =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "name",
            ["status"] = "status",
            ["lastcapturedat"] = "lastcapturedat",
            ["totalcapturedcount"] = "totalcapturedcount",
            ["creationtime"] = "creationtime",
        };

    internal static async Task<CdcAcceptSchemaResultDto> AcceptSchemaBaselineCoreAsync(
        Guid id,
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        PgOutputCdcCaptureService captureService,
        SqlServerCdcCaptureService? sqlServerCaptureService,
        MySqlBinlogCdcCaptureService? mySqlCaptureService,
        SwapMid.Notification.INotificationService? notificationService,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken cancellationToken)
    {
        var stream = await streamRepository.GetAsync(id, cancellationToken: cancellationToken);
        var source = await sourceRepository.GetAsync(stream.SourceId, cancellationToken: cancellationToken);
        var eval = await EvaluateSchemaForStreamAsync(
            captureService, sqlServerCaptureService, mySqlCaptureService, source, stream, cancellationToken);
        if (eval.CurrentJson == "[]")
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "未能读取到上游监控表或发布表的列定义，无法建立 Schema 基线。");
        }

        var hadDrift = stream.SchemaDriftDetected;
        stream.UpdateSchemaBaseline(eval.CurrentHash, eval.CurrentJson);
        await streamRepository.UpdateAsync(stream, autoSave: true, cancellationToken: cancellationToken);
        SwapMid.Monitoring.PlatformMetrics.CdcSchemaDriftEventsTotal.WithLabels("accepted").Inc();

        if (hadDrift)
        {
            await NotifySchemaAcceptedAsync(stream, eval.CurrentHash, notificationService, logger, cancellationToken);
        }

        return new CdcAcceptSchemaResultDto
        {
            StreamId = stream.Id,
            SchemaBaselineHash = eval.CurrentHash,
            AcceptedAtUtc = DateTime.UtcNow,
            Message = $"已更新上游 Schema 基线（{eval.CurrentHash[..12]}），可正常激活或继续捕获。",
        };
    }
}
