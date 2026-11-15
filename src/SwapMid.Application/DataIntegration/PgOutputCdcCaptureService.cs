using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly ILogger<PgOutputCdcCaptureService> _logger;

    public PgOutputCdcCaptureService(
        IEtlRelationalConnectionFactory connectionFactory,
        IIntegrationEventPublisher integrationEventPublisher,
        DataPipelineExecutionCoordinator executionCoordinator,
        ILogger<PgOutputCdcCaptureService> logger)
    {
        _connectionFactory = connectionFactory;
        _integrationEventPublisher = integrationEventPublisher;
        _executionCoordinator = executionCoordinator;
        _logger = logger;
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

        var state = await InspectServerStateAsync((NpgsqlConnection)conn, publicationName, slotName, cancellationToken);
        var diagnostics = BuildPreflightDiagnostics(stream, publicationName, slotName, state);

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
            Diagnostics = diagnostics,
            Summary = isReady ? "PostgreSQL pgoutput 逻辑复制前置条件就绪。" : "逻辑复制前置条件尚未就绪，请按诊断提示处理。",
        };
    }

    internal async Task<PgOutputCaptureBatchResult> CaptureBatchAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var publicationName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.PublicationName, "PublicationName");
        var slotName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.SlotName, "SlotName");
        var monitoredTables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables);
        EnsurePostgreSqlSource(source);

        try
        {
            ServerReplicationState serverState;
            await using (var adminConn = _connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30))
            {
                await adminConn.OpenAsync(cancellationToken);
                var npgsqlAdmin = (NpgsqlConnection)adminConn;
                serverState = await EnsureReplicationPreconditionsAsync(
                    npgsqlAdmin,
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
                    serverState = await InspectServerStateAsync((NpgsqlConnection)refreshConn, publicationName, slotName, cancellationToken);
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
            return 1;
        }

        return 0;
    }

    private static List<string> BuildPreflightDiagnostics(
        CdcStreamDefinition stream,
        string publicationName,
        string slotName,
        ServerReplicationState state)
    {
        var diagnostics = new List<string>();
        if (!string.Equals(state.WalLevel, "logical", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"当前 wal_level 为 \"{state.WalLevel}\"，需配置为 \"logical\" 并重启 PostgreSQL（ALTER SYSTEM SET wal_level = logical）。");
        }

        if (!state.PublicationExists)
        {
            diagnostics.Add(stream.AutoCreatePublicationAndSlot
                ? $"发布 \"{publicationName}\" 尚未创建，将在首次捕获时自动创建。"
                : $"发布 \"{publicationName}\" 不存在，请先手动执行 CREATE PUBLICATION 或开启自动建发布。");
        }

        if (!state.SlotExists)
        {
            diagnostics.Add(stream.AutoCreatePublicationAndSlot
                ? $"逻辑复制槽 \"{slotName}\"（pgoutput）尚未创建，将在首次捕获时自动创建。"
                : $"逻辑复制槽 \"{slotName}\" 不存在，请先创建或开启自动建槽。");
        }
        else if (!string.Equals(state.SlotPlugin, "pgoutput", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"复制槽 \"{slotName}\" 当前使用插件 \"{state.SlotPlugin}\"，并非 pgoutput。");
        }

        return diagnostics;
    }

    private static async Task<ServerReplicationState> EnsureReplicationPreconditionsAsync(
        NpgsqlConnection adminConn,
        CdcStreamDefinition stream,
        string publicationName,
        string slotName,
        IReadOnlyList<MonitoredTableSpec> monitoredTables,
        CancellationToken cancellationToken)
    {
        var serverState = await InspectServerStateAsync(adminConn, publicationName, slotName, cancellationToken);
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

    private static async Task<ServerReplicationState> InspectServerStateAsync(
        NpgsqlConnection conn,
        string publicationName,
        string slotName,
        CancellationToken cancellationToken)
    {
        string walLevel;
        string version;
        string? currentWalLsn;

        await using (var cmd = new NpgsqlCommand("SHOW wal_level", conn))
        {
            walLevel = Convert.ToString(await cmd.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
        }

        await using (var cmd = new NpgsqlCommand("SELECT version(), pg_current_wal_lsn()::text", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            version = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            currentWalLsn = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        bool pubExists;
        await using (var pubCmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_publication WHERE pubname = @pub)", conn))
        {
            pubCmd.Parameters.AddWithValue("pub", publicationName);
            pubExists = Convert.ToBoolean(await pubCmd.ExecuteScalarAsync(cancellationToken));
        }

        var slotExists = false;
        var slotActive = false;
        string? slotPlugin = null;
        string? confirmedFlushLsn = null;

        await using (var slotCmd = new NpgsqlCommand(
            "SELECT plugin, active, confirmed_flush_lsn::text FROM pg_replication_slots WHERE slot_name = @slot", conn))
        {
            slotCmd.Parameters.AddWithValue("slot", slotName);
            await using var reader = await slotCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                slotExists = true;
                slotPlugin = reader.IsDBNull(0) ? null : reader.GetString(0);
                slotActive = !reader.IsDBNull(1) && reader.GetBoolean(1);
                confirmedFlushLsn = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        return new ServerReplicationState(
            walLevel,
            version,
            currentWalLsn,
            pubExists,
            slotExists,
            slotActive,
            slotPlugin,
            confirmedFlushLsn);
    }

    private sealed record ServerReplicationState(
        string WalLevel,
        string Version,
        string? CurrentWalLsn,
        bool PublicationExists,
        bool SlotExists,
        bool SlotActive,
        string? SlotPlugin,
        string? ConfirmedFlushLsn);
}
