using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SwapMid.Monitoring;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// SQL Server 原生变更跟踪（CHANGETABLE(CHANGES ...)）增量捕获与一致性快照引擎。
/// </summary>
public sealed partial class SqlServerCdcCaptureService : ITransientDependency
{
    private const int ConnectTimeoutSeconds = 10;
    private const int CommandTimeoutSeconds = 60;

    private readonly IEtlRelationalConnectionFactory _connectionFactory;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly IReadOnlyDictionary<DataSourceType, IEtlSinkWriter> _sinkWriters;
    private readonly ILogger<SqlServerCdcCaptureService> _logger;

    public SqlServerCdcCaptureService(
        IEtlRelationalConnectionFactory connectionFactory,
        IIntegrationEventPublisher integrationEventPublisher,
        DataPipelineExecutionCoordinator executionCoordinator,
        IEnumerable<IEtlSinkWriter> sinkWriters,
        ILogger<SqlServerCdcCaptureService> logger)
    {
        _connectionFactory = connectionFactory;
        _integrationEventPublisher = integrationEventPublisher;
        _executionCoordinator = executionCoordinator;
        _sinkWriters = sinkWriters.ToDictionary(x => x.TargetType);
        _logger = logger;
    }

    internal static void EnsureSqlServerSource(DataSource source)
    {
        if (source.Type != DataSourceType.RelationalDatabase)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "CDC 源数据源必须为关系型数据库。");
        }

        var conn = RelationalDataSourceConnection.Parse(source.Config);
        if (!string.Equals(conn.NormalizedProvider, "sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"当前流配置为 SQL Server Change Tracking，但数据源协议为 {conn.NormalizedProvider}。");
        }
    }

    internal async Task<CdcPreflightResultDto> PreflightAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        EnsureSqlServerSource(source);
        var tables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables, defaultSchema: "dbo");
        var diagnostics = new List<string>();
        if (tables.Count == 0)
        {
            diagnostics.Add("SQL Server Change Tracking 模式下必须配置至少 1 张监听表（如 dbo.orders）。");
            return new CdcPreflightResultDto
            {
                IsReady = false,
                WalLevel = "change_tracking",
                Diagnostics = diagnostics,
                Summary = "SQL Server 变更跟踪前置条件未就绪。",
            };
        }

        await using var conn = (SqlConnection)_connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30);
        await conn.OpenAsync(cancellationToken);

        var dbState = await InspectServerAsync(conn, tables, stream.CheckpointLsn, cancellationToken);
        var schemaEval = await EvaluateSchemaCoreAsync(conn, tables, stream.SchemaBaselineJson, cancellationToken);
        diagnostics.AddRange(dbState.Diagnostics);

        var isReady = dbState.DatabaseCtEnabled && dbState.InvalidTables == 0 && !dbState.CheckpointExpired;
        if (isReady)
        {
            diagnostics.Add($"SQL Server Change Tracking 就绪（当前数据库版本 {dbState.CurrentVersion}）。");
        }

        return new CdcPreflightResultDto
        {
            IsReady = isReady,
            WalLevel = dbState.DatabaseCtEnabled ? "change_tracking" : "disabled",
            PostgreSqlVersion = dbState.Version,
            PublicationExists = dbState.DatabaseCtEnabled,
            SlotExists = dbState.InvalidTables == 0,
            SlotActive = false,
            SlotPlugin = "sqlserver_ct",
            ConfirmedFlushLsn = stream.CheckpointLsn,
            CurrentWalLsn = dbState.CurrentVersion?.ToString(CultureInfo.InvariantCulture),
            WalLagBytes = dbState.CurrentVersion.HasValue && long.TryParse(stream.CheckpointLsn, out var stored)
                ? Math.Max(0, dbState.CurrentVersion.Value - stored)
                : null,
            SchemaDriftDetected = schemaEval.HasDrift,
            SchemaDriftSummary = schemaEval.HasDrift ? schemaEval.Summary : null,
            CurrentSchemaHash = schemaEval.CurrentHash,
            Diagnostics = diagnostics,
            Summary = isReady ? "SQL Server Change Tracking 前置条件就绪。" : "SQL Server Change Tracking 前置条件尚未就绪，请按诊断提示处理。",
        };
    }

    internal async Task<CdcSchemaDriftEvaluation> EvaluateSchemaAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        EnsureSqlServerSource(source);
        var tables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables, defaultSchema: "dbo");
        await using var conn = (SqlConnection)_connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30);
        await conn.OpenAsync(cancellationToken);
        return await EvaluateSchemaCoreAsync(conn, tables, stream.SchemaBaselineJson, cancellationToken);
    }

    internal async Task<PgOutputSnapshotBatchResult> SnapshotAsync(
        DataSource source,
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken)
    {
        EnsureSqlServerSource(source);
        var tables = ParseRequiredMonitoredTables(stream.MonitoredTables);

        await using var conn = (SqlConnection)_connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, CommandTimeoutSeconds);
        await conn.OpenAsync(cancellationToken);

        var state = await EnsureReadyAsync(conn, tables, checkpoint: null, checkExpired: false, cancellationToken);
        var useSnapshotIso = await IsSnapshotIsolationEnabledAsync(conn, cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(
            useSnapshotIso ? IsolationLevel.Snapshot : IsolationLevel.RepeatableRead,
            cancellationToken);

        long snapVersion;
        await using (var verCmd = new SqlCommand("SELECT CHANGE_TRACKING_CURRENT_VERSION()", conn, (SqlTransaction)tx))
        {
            var rawVer = await verCmd.ExecuteScalarAsync(cancellationToken);
            snapVersion = rawVer is null or DBNull ? 0L : Convert.ToInt64(rawVer, CultureInfo.InvariantCulture);
        }

        var snapToken = snapVersion.ToString(CultureInfo.InvariantCulture);
        var chunkSize = Math.Clamp(stream.MaxBatchSize, 50, 2000);
        long totalRows = 0;
        long totalDelivered = 0;
        var chunkIndex = 0;
        var chunkBuffer = new List<PgOutputCapturedChange>(chunkSize);
        var tableHint = useSnapshotIso ? string.Empty : " WITH (HOLDLOCK)";

        foreach (var table in tables)
        {
            var meta = state.Tables[table.FullName];
            var selectCols = string.Join(", ", meta.AllColumns.Select(QuotePart));
            await using var cmd = new SqlCommand(
                $"SELECT {selectCols} FROM {QuotePart(table.Schema)}.{QuotePart(table.Table)}{tableHint}",
                conn,
                (SqlTransaction)tx);
            cmd.CommandTimeout = CommandTimeoutSeconds;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(meta.AllColumns.Count, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < meta.AllColumns.Count; i++)
                {
                    row[meta.AllColumns[i]] = await reader.IsDBNullAsync(i, cancellationToken)
                        ? null
                        : reader.GetValue(i);
                }

                chunkBuffer.Add(new PgOutputCapturedChange(
                    table.Schema, table.Table, "snapshot", snapToken, DateTime.UtcNow, row, null));
                totalRows++;

                if (chunkBuffer.Count >= chunkSize)
                {
                    chunkIndex++;
                    totalDelivered += await CdcStreamDeliveryDispatcher.DeliverAsync(
                        stream,
                        targetSink,
                        chunkBuffer,
                        batchEndLsn: null,
                        _integrationEventPublisher,
                        _executionCoordinator,
                        _sinkWriters,
                        _connectionFactory,
                        _logger,
                        cancellationToken,
                        eventSequenceOffset: totalRows - chunkBuffer.Count,
                        idempotencyKeyOverride: $"ct-snap:{snapToken}:chunk:{chunkIndex}",
                        triggerPipeline: false);
                    chunkBuffer.Clear();
                }
            }
        }

        if (chunkBuffer.Count > 0)
        {
            chunkIndex++;
            totalDelivered += await CdcStreamDeliveryDispatcher.DeliverAsync(
                stream,
                targetSink,
                chunkBuffer,
                batchEndLsn: null,
                _integrationEventPublisher,
                _executionCoordinator,
                _sinkWriters,
                _connectionFactory,
                _logger,
                cancellationToken,
                eventSequenceOffset: totalRows - chunkBuffer.Count,
                idempotencyKeyOverride: $"ct-snap:{snapToken}:chunk:{chunkIndex}",
                triggerPipeline: true);
        }

        await tx.CommitAsync(cancellationToken);
        return new PgOutputSnapshotBatchResult(
            snapToken,
            totalRows,
            totalDelivered,
            UsedExportedSnapshot: useSnapshotIso,
            DateTime.UtcNow,
            $"SQL Server Change Tracking 一致性快照完成：导出 {totalRows} 行（版本 {snapToken}），投递 {totalDelivered} 项。");
    }

    internal async Task<PgOutputCaptureBatchResult> CaptureBatchAsync(
        DataSource source,
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken,
        Func<CdcSchemaDriftEvaluation, Task>? onSchemaEvaluated = null)
    {
        var sw = Stopwatch.StartNew();
        EnsureSqlServerSource(source);
        var tables = ParseRequiredMonitoredTables(stream.MonitoredTables);

        try
        {
            await using var conn = (SqlConnection)_connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, CommandTimeoutSeconds);
            await conn.OpenAsync(cancellationToken);

            var state = await EnsureReadyAsync(conn, tables, stream.CheckpointLsn, checkExpired: true, cancellationToken);
            var schemaEval = await EvaluateSchemaCoreAsync(conn, tables, stream.SchemaBaselineJson, cancellationToken);
            if (onSchemaEvaluated != null)
            {
                await onSchemaEvaluated(schemaEval);
            }

            var currentVersion = state.CurrentVersion ?? 0L;
            var currentVerStr = currentVersion.ToString(CultureInfo.InvariantCulture);

            if (!long.TryParse(stream.CheckpointLsn, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sinceVersion))
            {
                sw.Stop();
                PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
                PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
                return new PgOutputCaptureBatchResult(
                    stream.CheckpointLsn,
                    currentVerStr,
                    0,
                    0,
                    DateTime.UtcNow,
                    $"已初始化 SQL Server Change Tracking 起始版本 {currentVerStr}。");
            }

            if (currentVersion <= sinceVersion)
            {
                sw.Stop();
                PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
                PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
                return new PgOutputCaptureBatchResult(
                    stream.CheckpointLsn,
                    currentVerStr,
                    0,
                    0,
                    DateTime.UtcNow,
                    $"版本 {currentVerStr} 无新增变更。");
            }

            var collected = await ReadChangesAsync(
                conn, tables, state.Tables, sinceVersion, currentVersion, stream.MaxBatchSize, cancellationToken);
            var ordered = collected.OrderBy(c => c.Version).ThenBy(c => c.Sequence).ToList();

            var truncated = ordered.Count > stream.MaxBatchSize;
            long nextVersion = currentVersion;
            if (truncated)
            {
                var boundaryVer = ordered[stream.MaxBatchSize - 1].Version;
                if (boundaryVer > sinceVersion)
                {
                    ordered = ordered.Where(x => x.Version < boundaryVer).ToList();
                    nextVersion = boundaryVer - 1;
                }
                else
                {
                    ordered = ordered.Take(stream.MaxBatchSize).ToList();
                    nextVersion = boundaryVer;
                }
            }

            var changes = ordered.Select(x => x.Change).ToList();
            long delivered = 0;
            var nextVerStr = nextVersion.ToString(CultureInfo.InvariantCulture);
            if (changes.Count > 0)
            {
                delivered = await CdcStreamDeliveryDispatcher.DeliverAsync(
                    stream,
                    targetSink,
                    changes,
                    batchEndLsn: null,
                    _integrationEventPublisher,
                    _executionCoordinator,
                    _sinkWriters,
                    _connectionFactory,
                    _logger,
                    cancellationToken,
                    idempotencyKeyOverride: $"ct:{nextVerStr}");

                foreach (var change in changes)
                {
                    var opLabel = change.Operation.ToLowerInvariant();
                    if (opLabel is "insert" or "update" or "delete")
                    {
                        PlatformMetrics.CdcEventsCapturedTotal.WithLabels(opLabel).Inc();
                    }
                }
            }

            sw.Stop();
            PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
            PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
            PlatformMetrics.CdcWalLagBytes.WithLabels(stream.Name).Set(Math.Max(0, currentVersion - nextVersion));

            return new PgOutputCaptureBatchResult(
                stream.CheckpointLsn,
                nextVerStr,
                changes.Count,
                delivered,
                DateTime.UtcNow,
                $"SQL Server CT 捕获 {changes.Count} 条变更，投递 {delivered} 次（版本 {sinceVersion} -> {nextVerStr}）。");
        }
        catch (BusinessException ex) when (ex.Code == SwapMidDomainErrorCodes.DataIntegration.CdcStreamSchemaDriftDetected)
        {
            throw;
        }
        catch
        {
            sw.Stop();
            PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
            PlatformMetrics.CdcCapturesTotal.WithLabels("failed").Inc();
            throw;
        }
    }

    private static IReadOnlyList<MonitoredTableSpec> ParseRequiredMonitoredTables(string? raw)
    {
        var tables = CdcProtocolHelpers.ParseMonitoredTables(raw, defaultSchema: "dbo");
        if (tables.Count == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "SQL Server Change Tracking 模式下必须指定至少 1 张监听表（示例：dbo.orders）。");
        }

        return tables;
    }

    private static string QuotePart(string identifier)
        => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
