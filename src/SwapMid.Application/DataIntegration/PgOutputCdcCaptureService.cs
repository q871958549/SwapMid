using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using NpgsqlTypes;
using SwapMid.Monitoring;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

internal sealed record PgOutputCaptureBatchResult(
    string? PreviousCheckpointLsn,
    string? NewCheckpointLsn,
    long CapturedEventCount,
    long DeliveredCount,
    DateTime CapturedAtUtc,
    string Message);

/// <summary>
/// 基于 Npgsql 10 原生 <see cref="LogicalReplicationConnection"/> 的 PostgreSQL <c>pgoutput</c> 有界增量捕获引擎。
/// </summary>
public sealed class PgOutputCdcCaptureService : ITransientDependency
{
    private const int ConnectTimeoutSeconds = 10;

    private readonly IEtlRelationalConnectionFactory _connectionFactory;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly IReadOnlyDictionary<DataSourceType, IEtlSinkWriter> _sinkWriters;
    private readonly ILogger<PgOutputCdcCaptureService> _logger;

    public PgOutputCdcCaptureService(
        IEtlRelationalConnectionFactory connectionFactory,
        IIntegrationEventPublisher integrationEventPublisher,
        DataPipelineExecutionCoordinator executionCoordinator,
        ILogger<PgOutputCdcCaptureService> logger,
        IEnumerable<IEtlSinkWriter>? sinkWriters = null)
    {
        _connectionFactory = connectionFactory;
        _integrationEventPublisher = integrationEventPublisher;
        _executionCoordinator = executionCoordinator;
        _logger = logger;
        _sinkWriters = (sinkWriters ?? Array.Empty<IEtlSinkWriter>())
            .ToDictionary(x => x.TargetType);
    }

    internal static void EnsurePostgreSqlSource(DataSource source)
    {
        if (source.Type != DataSourceType.RelationalDatabase)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "CDC 仅支持关系型 PostgreSQL 数据源。");
        }

        RelationalDataSourceConnection.Probe probe;
        try
        {
            probe = RelationalDataSourceConnection.Parse(source.Config);
        }
        catch (Exception ex)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"无法解析数据源连接配置（{ex.GetType().Name}）。");
        }

        if (probe.NormalizedProvider is not ("postgresql" or "postgres" or "npgsql"))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"当前 CDC 引擎仅支持 PostgreSQL，不支持 {probe.NormalizedProvider}。");
        }
    }

    internal async Task<CdcPreflightResultDto> PreflightAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        var publicationName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.PublicationName, "PublicationName");
        var slotName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.SlotName, "SlotName");
        var monitoredTables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables);
        EnsurePostgreSqlSource(source);

        await using var conn = _connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30);
        try
        {
            await conn.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return new CdcPreflightResultDto
            {
                IsReady = false,
                Diagnostics = [$"连接 PostgreSQL 失败（{ex.GetType().Name}），请检查网络、端口与凭据配置。"],
                Summary = "PostgreSQL 数据源连接失败。",
            };
        }

        var state = await PgOutputReplicationMetadataReader.InspectServerStateAsync(
            (NpgsqlConnection)conn,
            publicationName,
            slotName,
            monitoredTables,
            cancellationToken);
        var diagnostics = PgOutputReplicationMetadataReader.BuildPreflightDiagnostics(
            stream,
            publicationName,
            slotName,
            state);

        if (state.WalLagBytes.HasValue)
        {
            PlatformMetrics.CdcWalLagBytes.WithLabels(stream.Name).Set(state.WalLagBytes.Value);
        }

        var walReady = string.Equals(state.WalLevel, "logical", StringComparison.OrdinalIgnoreCase);
        var pubReady = state.PublicationExists || stream.AutoCreatePublicationAndSlot;
        var slotReady = (state.SlotExists && string.Equals(state.SlotPlugin, "pgoutput", StringComparison.OrdinalIgnoreCase))
                        || (!state.SlotExists && stream.AutoCreatePublicationAndSlot);

        var isReady = walReady && pubReady && slotReady;
        if (isReady && diagnostics.Count == 0)
        {
            diagnostics.Add($"复制环境就绪（wal_level=logical，当前 WAL 位点 {state.CurrentWalLsn ?? "N/A"}）。");
        }

        return new CdcPreflightResultDto
        {
            IsReady = isReady,
            WalLevel = state.WalLevel,
            PostgreSqlVersion = state.Version,
            PublicationExists = state.PublicationExists,
            SlotExists = state.SlotExists,
            SlotActive = state.SlotActive,
            SlotPlugin = state.SlotPlugin,
            ConfirmedFlushLsn = state.ConfirmedFlushLsn,
            CurrentWalLsn = state.CurrentWalLsn,
            WalLagBytes = state.WalLagBytes,
            TableReplicaIdentities = state.TableReplicaIdentities,
            Diagnostics = diagnostics,
            Summary = isReady ? "PostgreSQL pgoutput 逻辑复制前置条件就绪。" : "逻辑复制前置条件尚未就绪，请按诊断提示处理。",
        };
    }

    internal async Task<CdcDropSlotResultDto> DropSlotAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        var slotName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.SlotName, "SlotName");
        EnsurePostgreSqlSource(source);

        await using var conn = _connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30);
        await conn.OpenAsync(cancellationToken);

        var (dropped, message) = await PgOutputReplicationMetadataReader.DropReplicationSlotAsync(
            (NpgsqlConnection)conn,
            slotName,
            cancellationToken);
        PlatformMetrics.CdcWalLagBytes.WithLabels(stream.Name).Set(0);

        return new CdcDropSlotResultDto
        {
            StreamId = stream.Id,
            SlotName = slotName,
            Dropped = dropped,
            Message = message,
        };
    }

    internal Task<PgOutputCaptureBatchResult> CaptureBatchAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
        => CaptureBatchAsync(source, stream, targetSink: null, cancellationToken);

    internal async Task<PgOutputCaptureBatchResult> CaptureBatchAsync(
        DataSource source,
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var publicationName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.PublicationName, "PublicationName");
        var slotName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.SlotName, "SlotName");
        var monitoredTables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables);
        EnsurePostgreSqlSource(source);

        try
        {
            PgServerReplicationState serverState;
            await using (var adminConn = _connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30))
            {
                await adminConn.OpenAsync(cancellationToken);
                serverState = await EnsureReplicationPreconditionsAsync(
                    (NpgsqlConnection)adminConn,
                    stream,
                    publicationName,
                    slotName,
                    monitoredTables,
                    cancellationToken);
            }

            // 通过工厂构造同款连接参数串供 LogicalReplicationConnection 使用（带物理连接隔离，Pooling=false）。
            string replConnString;
            await using (var probeConn = _connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30))
            {
                replConnString = probeConn.ConnectionString;
            }

            (List<PgOutputCapturedChange> Changes, NpgsqlLogSequenceNumber? LastCommittedLsn) readOutcome;
            await using (var replConn = new LogicalReplicationConnection(replConnString))
            {
                await replConn.Open(cancellationToken);

                PgOutputReplicationSlot slot;
                if (!serverState.SlotExists)
                {
                    if (!stream.AutoCreatePublicationAndSlot)
                    {
                        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                            .WithData("Reason", $"复制槽 \"{slotName}\" 不存在且未启用自动创建。");
                    }

                    slot = await replConn.CreatePgOutputReplicationSlot(
                        slotName,
                        temporarySlot: false,
                        slotSnapshotInitMode: LogicalSlotSnapshotInitMode.NoExport,
                        twoPhase: false,
                        cancellationToken: cancellationToken);

                    await using var refreshConn = _connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30);
                    await refreshConn.OpenAsync(cancellationToken);
                    serverState = await PgOutputReplicationMetadataReader.InspectServerStateAsync(
                        (NpgsqlConnection)refreshConn,
                        publicationName,
                        slotName,
                        monitoredTables,
                        cancellationToken);
                }
                else
                {
                    slot = new PgOutputReplicationSlot(slotName);
                }

                var startLsn = PgOutputWalStreamReader.DetermineStartLsn(stream.CheckpointLsn, serverState.ConfirmedFlushLsn);
                var headWalLsn = CdcProtocolHelpers.TryParseLsn(serverState.CurrentWalLsn);

                readOutcome = await PgOutputWalStreamReader.ReadCommittedChangesAsync(
                    replConn,
                    slot,
                    publicationName,
                    monitoredTables,
                    stream.MaxBatchSize,
                    startLsn,
                    headWalLsn,
                    _logger,
                    cancellationToken);
            }

            long deliveredCount = 0;
            if (readOutcome.Changes.Count > 0)
            {
                deliveredCount = await DeliverChangesAsync(
                    stream,
                    targetSink,
                    readOutcome.Changes,
                    readOutcome.LastCommittedLsn,
                    cancellationToken);
            }

            if (readOutcome.LastCommittedLsn.HasValue)
            {
                await using var advanceConn = _connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30);
                await advanceConn.OpenAsync(cancellationToken);
                await PgOutputWalStreamReader.AdvanceSlotCheckpointAsync(
                    (NpgsqlConnection)advanceConn,
                    slotName,
                    readOutcome.LastCommittedLsn.Value,
                    cancellationToken);
                var postState = await PgOutputReplicationMetadataReader.InspectServerStateAsync(
                    (NpgsqlConnection)advanceConn,
                    publicationName,
                    slotName,
                    monitoredTables,
                    cancellationToken);
                if (postState.WalLagBytes.HasValue)
                {
                    PlatformMetrics.CdcWalLagBytes.WithLabels(stream.Name).Set(postState.WalLagBytes.Value);
                }
            }
            else if (serverState.WalLagBytes.HasValue)
            {
                PlatformMetrics.CdcWalLagBytes.WithLabels(stream.Name).Set(serverState.WalLagBytes.Value);
            }

            foreach (var change in readOutcome.Changes)
            {
                PlatformMetrics.CdcEventsCapturedTotal.WithLabels(change.Operation).Inc();
            }

            PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
            PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);

            var newCheckpoint = readOutcome.LastCommittedLsn?.ToString()
                                ?? stream.CheckpointLsn
                                ?? serverState.ConfirmedFlushLsn;
            return new PgOutputCaptureBatchResult(
                stream.CheckpointLsn,
                newCheckpoint,
                readOutcome.Changes.Count,
                deliveredCount,
                DateTime.UtcNow,
                readOutcome.Changes.Count > 0
                    ? $"已捕获 {readOutcome.Changes.Count} 条已提交 WAL 变更（投递 {deliveredCount}），断点更新至 {newCheckpoint}。"
                    : $"上游 WAL 已追平，本次无新增变更（当前断点 {newCheckpoint ?? "N/A"}）。");
        }
        catch (Exception)
        {
            PlatformMetrics.CdcCapturesTotal.WithLabels("failed").Inc();
            PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
            throw;
        }
    }

    private async Task<long> DeliverChangesAsync(
        CdcStreamDefinition stream,
        DataSource? targetSink,
        IReadOnlyList<PgOutputCapturedChange> changes,
        NpgsqlLogSequenceNumber? batchEndLsn,
        CancellationToken cancellationToken)
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
                var eventId = CdcProtocolHelpers.BuildDeterministicEventId(
                    stream.Id,
                    change.Lsn,
                    i,
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

                var accepted = await _integrationEventPublisher.PublishIfTopicActiveAsync(
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
            if (!stream.TargetPipelineId.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "DataPipeline 目标模式下必须选择关联的数据管道。");
            }

            var endLsnText = batchEndLsn?.ToString() ?? changes[^1].Lsn;
            var runId = CdcProtocolHelpers.BuildDeterministicPipelineRunId(stream.Id, endLsnText);
            await _executionCoordinator.ExecuteAsync(
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
                _sinkWriters,
                cancellationToken);
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

    private static async Task<PgServerReplicationState> EnsureReplicationPreconditionsAsync(
        NpgsqlConnection adminConn,
        CdcStreamDefinition stream,
        string publicationName,
        string slotName,
        IReadOnlyList<MonitoredTableSpec> monitoredTables,
        CancellationToken cancellationToken)
    {
        var serverState = await PgOutputReplicationMetadataReader.InspectServerStateAsync(
            adminConn,
            publicationName,
            slotName,
            monitoredTables,
            cancellationToken);
        if (!string.Equals(serverState.WalLevel, "logical", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", $"上游 PostgreSQL wal_level 为 \"{serverState.WalLevel}\"，需设置为 logical。");
        }

        if (!serverState.PublicationExists)
        {
            if (!stream.AutoCreatePublicationAndSlot)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", $"发布 \"{publicationName}\" 不存在且未启用自动创建。");
            }

            var createPubSql = CdcProtocolHelpers.BuildCreatePublicationSql(publicationName, monitoredTables);
            await using var pubCmd = new NpgsqlCommand(createPubSql, adminConn);
            await pubCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (serverState.SlotExists &&
            !string.Equals(serverState.SlotPlugin, "pgoutput", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", $"复制槽 \"{slotName}\" 插件为 \"{serverState.SlotPlugin}\"，仅支持 pgoutput。");
        }

        return serverState;
    }
}
