using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SwapMid.Monitoring;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// MySQL / MariaDB 二进制日志（ROW-based Binlog）原生捕获与一致性快照引擎：
/// 基于 <c>MySqlCdc.BinlogClient</c> 与 <see cref="MySqlConnector"/> 实现只读快照、事务边界增量同步、
/// Purge 检测及 Schema 漂移识别。
/// </summary>
public sealed partial class MySqlBinlogCdcCaptureService : ITransientDependency
{
    private const int ConnectTimeoutSeconds = 10;
    private const int CommandTimeoutSeconds = 60;
    private const int BinlogReadTimeoutSeconds = 20;

    private readonly IEtlRelationalConnectionFactory _connectionFactory;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly IReadOnlyDictionary<DataSourceType, IEtlSinkWriter> _sinkWriters;
    private readonly ILogger<MySqlBinlogCdcCaptureService> _logger;

    public MySqlBinlogCdcCaptureService(
        IEtlRelationalConnectionFactory connectionFactory,
        IIntegrationEventPublisher integrationEventPublisher,
        DataPipelineExecutionCoordinator executionCoordinator,
        IEnumerable<IEtlSinkWriter> sinkWriters,
        ILogger<MySqlBinlogCdcCaptureService> logger)
    {
        _connectionFactory = connectionFactory;
        _integrationEventPublisher = integrationEventPublisher;
        _executionCoordinator = executionCoordinator;
        _sinkWriters = sinkWriters.ToDictionary(x => x.TargetType);
        _logger = logger;
    }

    internal static void EnsureMySqlSource(DataSource source)
    {
        if (source.Type != DataSourceType.RelationalDatabase)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "CDC 源数据源必须为关系型数据库。");
        }

        var conn = RelationalDataSourceConnection.Parse(source.Config);
        if (conn.NormalizedProvider is not ("mysql" or "mariadb"))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"当前流配置为 MySQL Binlog，但数据源协议为 {conn.NormalizedProvider}。");
        }
    }

    internal async Task<CdcSchemaDriftEvaluation> EvaluateSchemaAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        EnsureMySqlSource(source);
        var probe = RelationalDataSourceConnection.Parse(source.Config);
        var tables = ParseRequiredTables(stream.MonitoredTables, probe.Database);
        await using var conn = (MySqlConnection)_connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30);
        await conn.OpenAsync(cancellationToken);

        var map = new Dictionary<string, MySqlTableMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in tables)
        {
            map[spec.FullName] = await LoadTableMetadataAsync(conn, spec.Schema, spec.Table, cancellationToken);
        }

        return EvaluateSchemaFromTables(map, tables, stream.SchemaBaselineJson);
    }

    internal async Task<CdcPreflightResultDto> PreflightAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        EnsureMySqlSource(source);
        var probe = RelationalDataSourceConnection.Parse(source.Config);
        var tables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables, defaultSchema: probe.Database ?? string.Empty);
        if (tables.Count == 0 || tables.Any(t => string.IsNullOrWhiteSpace(t.Schema)))
        {
            return new CdcPreflightResultDto
            {
                IsReady = false,
                WalLevel = "binlog",
                Diagnostics = ["MySQL Binlog 模式下必须配置至少 1 张含库名监听表（如 appdb.orders）。"],
                Summary = "MySQL Binlog 前置条件未就绪。",
            };
        }

        await using var conn = (MySqlConnection)_connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30);
        await conn.OpenAsync(cancellationToken);

        var state = await InspectServerAsync(conn, tables, stream.CheckpointLsn, cancellationToken);
        var schemaEval = EvaluateSchemaFromTables(state.Tables, tables, stream.SchemaBaselineJson);

        if (state.WalLagBytes.HasValue)
        {
            PlatformMetrics.CdcWalLagBytes.WithLabels(stream.Name).Set(state.WalLagBytes.Value);
        }

        var isReady = state.LogBinEnabled &&
                      string.Equals(state.BinlogFormat, "ROW", StringComparison.OrdinalIgnoreCase) &&
                      state.InvalidTables == 0 &&
                      !state.CheckpointExpired &&
                      !string.IsNullOrWhiteSpace(state.ActiveFile);

        var diagnostics = new List<string>(state.Diagnostics);
        if (schemaEval.HasDrift)
        {
            diagnostics.Add($"上游表结构存在 Schema 漂移：{schemaEval.Summary}");
        }

        if (isReady)
        {
            diagnostics.Add(
                $"MySQL Binlog 就绪（binlog_format={state.BinlogFormat}, row_image={state.BinlogRowImage ?? "N/A"}, 位点={state.ActiveFile}:{state.ActivePosition}）。");
        }

        var currentToken = !string.IsNullOrWhiteSpace(state.ActiveFile) && state.ActivePosition.HasValue
            ? $"{state.ActiveFile}:{state.ActivePosition.Value}"
            : null;

        return new CdcPreflightResultDto
        {
            IsReady = isReady,
            WalLevel = state.LogBinEnabled ? $"binlog:{state.BinlogFormat ?? "UNKNOWN"}" : "disabled",
            PostgreSqlVersion = state.Version,
            PublicationExists = state.LogBinEnabled,
            SlotExists = isReady,
            SlotActive = isReady,
            SlotPlugin = "mysql_binlog",
            ConfirmedFlushLsn = stream.CheckpointLsn,
            CurrentWalLsn = currentToken,
            WalLagBytes = state.WalLagBytes,
            TableReplicaIdentities = tables.Select(t =>
            {
                var meta = state.Tables[t.FullName];
                var idMode = meta.PrimaryKeys.Count > 0 ? $"PK({string.Join(",", meta.PrimaryKeys)})" : "NO_PK";
                return new CdcTableReplicaIdentityDto
                {
                    Schema = t.Schema,
                    Table = t.Table,
                    ReplicaIdentity = $"{state.BinlogRowImage ?? "FULL"}|{idMode}",
                };
            }).ToList(),
            SchemaDriftDetected = stream.SchemaDriftDetected || schemaEval.HasDrift,
            SchemaDriftSummary = schemaEval.HasDrift ? schemaEval.Summary : stream.SchemaDriftSummary,
            CurrentSchemaHash = schemaEval.CurrentHash,
            Diagnostics = diagnostics,
            Summary = isReady
                ? "MySQL Binlog 检查通过，可执行一致快照或增量捕获。"
                : "MySQL Binlog 前置条件未全部满足，请查看诊断信息。",
        };
    }

    internal async Task<PgOutputSnapshotBatchResult> SnapshotAsync(
        DataSource source,
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken)
    {
        EnsureMySqlSource(source);
        var probe = RelationalDataSourceConnection.Parse(source.Config);
        var tables = ParseRequiredTables(stream.MonitoredTables, probe.Database);

        await using var conn = (MySqlConnection)_connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 300);
        await conn.OpenAsync(cancellationToken);
        var state = await EnsureReadyAsync(conn, tables, stream.CheckpointLsn, checkExpired: false, cancellationToken);

        bool usedFtwrl = false;
        string snapToken;
        long totalRows = 0;
        long totalDelivered = 0;
        var chunkSize = Math.Clamp(stream.MaxBatchSize, 50, 2000);
        var chunkBuffer = new List<PgOutputCapturedChange>(chunkSize);
        var chunkIndex = 0;

        try
        {
            try
            {
                await using (var lockCmd = new MySqlCommand("FLUSH TABLES WITH READ LOCK", conn))
                {
                    await lockCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await ExecuteNonQueryAsync(conn, "SET SESSION TRANSACTION ISOLATION LEVEL REPEATABLE READ", cancellationToken);
                await ExecuteNonQueryAsync(conn, "START TRANSACTION WITH CONSISTENT SNAPSHOT", cancellationToken);
                var (file, pos) = await ReadActiveMasterPositionAsync(conn, cancellationToken);
                await ExecuteNonQueryAsync(conn, "UNLOCK TABLES", cancellationToken);
                usedFtwrl = true;
                snapToken = $"{file}:{pos ?? 4}";
            }
            catch (MySqlException)
            {
                var lockTargets = string.Join(
                    ", ",
                    tables.Select(t => $"{QuoteMySql(t.Schema)}.{QuoteMySql(t.Table)} READ"));
                await ExecuteNonQueryAsync(conn, $"LOCK TABLES {lockTargets}", cancellationToken);
                var (file, pos) = await ReadActiveMasterPositionAsync(conn, cancellationToken);
                snapToken = $"{file}:{pos ?? 4}";
            }

            foreach (var spec in tables)
            {
                var meta = state.Tables[spec.FullName];
                var selectCols = string.Join(", ", meta.Columns.Select(c => QuoteMySql(c.Name)));
                await using var cmd = new MySqlCommand(
                    $"SELECT {selectCols} FROM {QuoteMySql(spec.Schema)}.{QuoteMySql(spec.Table)}",
                    conn);
                cmd.CommandTimeout = 300;
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object?>(meta.Columns.Count, StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < meta.Columns.Count; i++)
                    {
                        var val = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                        row[meta.Columns[i].Name] = ConvertMySqlCellValue(val, meta.Columns[i].DataType);
                    }

                    chunkBuffer.Add(new PgOutputCapturedChange(
                        spec.Schema, spec.Table, "snapshot", snapToken, DateTime.UtcNow, row, null));
                    totalRows++;

                    if (chunkBuffer.Count >= chunkSize)
                    {
                        chunkIndex++;
                        totalDelivered += await CdcStreamDeliveryDispatcher.DeliverAsync(
                            stream, targetSink, chunkBuffer, null,
                            _integrationEventPublisher, _executionCoordinator, _sinkWriters,
                            _connectionFactory, _logger, cancellationToken,
                            eventSequenceOffset: totalRows - chunkBuffer.Count,
                            idempotencyKeyOverride: $"mysql-snap:{snapToken}:chunk:{chunkIndex}",
                            triggerPipeline: false);
                        chunkBuffer.Clear();
                    }
                }
            }

            if (chunkBuffer.Count > 0)
            {
                chunkIndex++;
                totalDelivered += await CdcStreamDeliveryDispatcher.DeliverAsync(
                    stream, targetSink, chunkBuffer, null,
                    _integrationEventPublisher, _executionCoordinator, _sinkWriters,
                    _connectionFactory, _logger, cancellationToken,
                    eventSequenceOffset: totalRows - chunkBuffer.Count,
                    idempotencyKeyOverride: $"mysql-snap:{snapToken}:chunk:{chunkIndex}",
                    triggerPipeline: true);
            }

            if (usedFtwrl)
            {
                await ExecuteNonQueryAsync(conn, "COMMIT", cancellationToken);
            }
            else
            {
                await ExecuteNonQueryAsync(conn, "UNLOCK TABLES", cancellationToken);
            }
        }
        finally
        {
            if (!usedFtwrl)
            {
                try { await ExecuteNonQueryAsync(conn, "UNLOCK TABLES", CancellationToken.None); } catch { }
            }
        }

        return new PgOutputSnapshotBatchResult(
            snapToken,
            totalRows,
            totalDelivered,
            UsedExportedSnapshot: usedFtwrl,
            DateTime.UtcNow,
            $"MySQL Binlog 一致性快照完成：导出 {totalRows} 行（位点 {snapToken}），投递 {totalDelivered} 项。");
    }

    internal async Task<PgOutputCaptureBatchResult> CaptureBatchAsync(
        DataSource source,
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken,
        Func<CdcSchemaDriftEvaluation, Task>? onSchemaEvaluated = null)
    {
        var sw = Stopwatch.StartNew();
        EnsureMySqlSource(source);
        var probe = RelationalDataSourceConnection.Parse(source.Config);
        var tables = ParseRequiredTables(stream.MonitoredTables, probe.Database);

        try
        {
            await using var conn = (MySqlConnection)_connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, CommandTimeoutSeconds);
            await conn.OpenAsync(cancellationToken);

            var state = await EnsureReadyAsync(conn, tables, stream.CheckpointLsn, checkExpired: true, cancellationToken);
            var schemaEval = EvaluateSchemaFromTables(state.Tables, tables, stream.SchemaBaselineJson);
            if (onSchemaEvaluated != null)
            {
                await onSchemaEvaluated(schemaEval);
            }

            if (string.IsNullOrWhiteSpace(state.ActiveFile) || !state.ActivePosition.HasValue)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", "MySQL 源库未返回当前活跃 Binlog 位点。");
            }

            var activeCheckpoint = new MySqlBinlogCheckpoint(state.ActiveFile, state.ActivePosition.Value);
            var stored = TryParseCheckpoint(stream.CheckpointLsn);
            if (stored == null)
            {
                sw.Stop();
                PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
                PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
                return new PgOutputCaptureBatchResult(
                    stream.CheckpointLsn,
                    activeCheckpoint.Token,
                    0,
                    0,
                    DateTime.UtcNow,
                    $"已初始化 MySQL Binlog 起始位点 {activeCheckpoint.Token}。");
            }

            if (string.Equals(stored.Filename, activeCheckpoint.Filename, StringComparison.Ordinal) &&
                stored.Position >= activeCheckpoint.Position)
            {
                sw.Stop();
                PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
                PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
                return new PgOutputCaptureBatchResult(
                    stream.CheckpointLsn,
                    activeCheckpoint.Token,
                    0,
                    0,
                    DateTime.UtcNow,
                    $"位点 {activeCheckpoint.Token} 无新增 Binlog 变更。");
            }

            var (changes, committedCheckpoint) = await ReadBinlogBatchAsync(
                probe,
                stream,
                tables,
                state.Tables,
                stored,
                activeCheckpoint,
                cancellationToken);

            long delivered = 0;
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
                    cancellationToken);
            }

            sw.Stop();
            PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
            PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
            if (changes.Count > 0)
            {
                PlatformMetrics.CdcEventsCapturedTotal.WithLabels(stream.Name).Inc(changes.Count);
            }

            return new PgOutputCaptureBatchResult(
                stream.CheckpointLsn,
                committedCheckpoint.Token,
                changes.Count,
                delivered,
                DateTime.UtcNow,
                changes.Count > 0
                    ? $"MySQL Binlog 捕获 {changes.Count} 条变更（{stored.Token} → {committedCheckpoint.Token}），投递 {delivered} 项。"
                    : $"Binlog 已检查至位点 {committedCheckpoint.Token}，无匹配的监听表变更。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            sw.Stop();
            PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
            PlatformMetrics.CdcCapturesTotal.WithLabels("failed").Inc();
            throw;
        }
    }
}
