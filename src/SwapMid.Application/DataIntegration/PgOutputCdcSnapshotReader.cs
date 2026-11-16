using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using NpgsqlTypes;
using Volo.Abp;

namespace SwapMid.DataIntegration;

internal sealed record PgOutputSnapshotBatchResult(
    string? ConsistentPointLsn,
    long SnapshotRowCount,
    long DeliveredCount,
    bool UsedExportedSnapshot,
    DateTime CompletedAtUtc,
    string Message);

/// <summary>
/// 基于 PostgreSQL 逻辑复制 <c>EXPORT_SNAPSHOT</c> + <c>REPEATABLE READ</c> 的全增量一致基线快照读取器。
/// </summary>
internal static partial class PgOutputCdcSnapshotReader
{
    private const int ConnectTimeoutSeconds = 10;

    [GeneratedRegex("^[0-9A-Za-z_-]{1,128}$", RegexOptions.Compiled)]
    private static partial Regex SnapshotIdRegex();

    internal static async Task<PgOutputSnapshotBatchResult> CaptureConsistentSnapshotAsync(
        IEtlRelationalConnectionFactory connectionFactory,
        DataSource source,
        CdcStreamDefinition stream,
        Func<IReadOnlyList<PgOutputCapturedChange>, NpgsqlLogSequenceNumber?, long, string, bool, CancellationToken, Task<long>> deliverChunkAsync,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var publicationName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.PublicationName, "PublicationName");
        var slotName = CdcProtocolHelpers.ValidateReplicationIdentifier(stream.SlotName, "SlotName");
        var configuredTables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables);
        var chunkSize = Math.Clamp(stream.MaxBatchSize, CdcStreamConsts.MinBatchSize, CdcStreamConsts.MaxBatchSize);

        PgServerReplicationState serverState;
        await using (var adminConn = connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30))
        {
            await adminConn.OpenAsync(cancellationToken);
            serverState = await PgOutputCdcCaptureService.EnsureReplicationPreconditionsAsync(
                (NpgsqlConnection)adminConn,
                stream,
                publicationName,
                slotName,
                configuredTables,
                cancellationToken);
        }

        string replConnString;
        await using (var probeConn = connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 30))
        {
            replConnString = probeConn.ConnectionString;
        }

        LogicalReplicationConnection? replConn = null;
        try
        {
            string? snapshotName = null;
            NpgsqlLogSequenceNumber? consistentLsn = null;
            var usedExportedSnapshot = false;

            if (!serverState.SlotExists)
            {
                if (!stream.AutoCreatePublicationAndSlot)
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                        .WithData("Reason", $"复制槽 \"{slotName}\" 不存在且未启用自动创建。");
                }

                replConn = new LogicalReplicationConnection(replConnString);
                await replConn.Open(cancellationToken);
                var slot = await replConn.CreatePgOutputReplicationSlot(
                    slotName,
                    temporarySlot: false,
                    slotSnapshotInitMode: LogicalSlotSnapshotInitMode.Export,
                    twoPhase: false,
                    cancellationToken: cancellationToken);
                snapshotName = slot.SnapshotName;
                consistentLsn = slot.ConsistentPoint;
                if (string.IsNullOrWhiteSpace(snapshotName) || !SnapshotIdRegex().IsMatch(snapshotName))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                        .WithData("Reason", "PostgreSQL 未返回有效的逻辑复制导出快照标识（EXPORT_SNAPSHOT）。");
                }

                usedExportedSnapshot = true;
            }

            await using var snapConn = (NpgsqlConnection)connectionFactory.CreateTarget(source.Config, ConnectTimeoutSeconds, 300);
            await snapConn.OpenAsync(cancellationToken);
            await using var tx = await snapConn.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

            if (usedExportedSnapshot)
            {
                await using var setCmd = new NpgsqlCommand(
                    $"SET TRANSACTION READ ONLY; SET TRANSACTION SNAPSHOT '{snapshotName}';",
                    snapConn,
                    tx);
                await setCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var roCmd = new NpgsqlCommand("SET TRANSACTION READ ONLY", snapConn, tx);
                await roCmd.ExecuteNonQueryAsync(cancellationToken);
                await using var lsnCmd = new NpgsqlCommand("SELECT pg_current_wal_lsn()::text", snapConn, tx);
                var lsnObj = await lsnCmd.ExecuteScalarAsync(cancellationToken);
                consistentLsn = CdcProtocolHelpers.TryParseLsn(Convert.ToString(lsnObj));
            }

            var tables = configuredTables.Count > 0
                ? configuredTables
                : await LoadPublicationTablesAsync(snapConn, tx, publicationName, cancellationToken);
            if (tables.Count == 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "监听表清单为空且发布中未包含任何用户表，无法执行全量基线快照。");
            }

            var lsnText = consistentLsn?.ToString() ?? stream.CheckpointLsn ?? "0/0";
            var timestampUtc = DateTime.UtcNow;
            long totalRows = 0;
            long totalDelivered = 0;
            var chunkIndex = 0;

            foreach (var table in tables)
            {
                var safeSchema = ValidateTablePart(table.Schema);
                var safeTable = ValidateTablePart(table.Table);
                var sql = $"SELECT * FROM \"{safeSchema}\".\"{safeTable}\"";

                await using var cmd = new NpgsqlCommand(sql, snapConn, tx);
                await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
                var columnNames = new string[reader.FieldCount];
                for (var c = 0; c < reader.FieldCount; c++)
                {
                    columnNames[c] = reader.GetName(c);
                }

                var buffer = new List<PgOutputCapturedChange>(chunkSize);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var rowDict = new Dictionary<string, object?>(columnNames.Length, StringComparer.Ordinal);
                    for (var c = 0; c < columnNames.Length; c++)
                    {
                        rowDict[columnNames[c]] = await reader.IsDBNullAsync(c, cancellationToken)
                            ? null
                            : NormalizeValue(reader.GetValue(c));
                    }

                    buffer.Add(new PgOutputCapturedChange(
                        table.Schema,
                        table.Table,
                        "snapshot",
                        lsnText,
                        timestampUtc,
                        rowDict,
                        null));

                    if (buffer.Count >= chunkSize)
                    {
                        var seqOffset = totalRows;
                        totalRows += buffer.Count;
                        totalDelivered += await deliverChunkAsync(
                            buffer,
                            consistentLsn,
                            seqOffset,
                            $"{lsnText}#snap-{chunkIndex}",
                            false,
                            cancellationToken);
                        chunkIndex++;
                        buffer.Clear();
                    }
                }

                if (buffer.Count > 0)
                {
                    var seqOffset = totalRows;
                    totalRows += buffer.Count;
                    totalDelivered += await deliverChunkAsync(
                        buffer,
                        consistentLsn,
                        seqOffset,
                        $"{lsnText}#snap-{chunkIndex}",
                        false,
                        cancellationToken);
                    chunkIndex++;
                    buffer.Clear();
                }
            }

            if (totalRows > 0)
            {
                // Flush pipeline trigger once when downstream is a DataPipeline (no-op for Topic/ExternalSink).
                totalDelivered += await deliverChunkAsync(
                    Array.Empty<PgOutputCapturedChange>(),
                    consistentLsn,
                    totalRows,
                    $"{lsnText}#snap-complete",
                    true,
                    cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

            var modeDesc = usedExportedSnapshot
                ? $"EXPORT_SNAPSHOT 一致点 {lsnText}"
                : $"REPEATABLE READ 位点 {lsnText}";
            logger.LogInformation(
                "CDC 流 {StreamName} 完成全量基线快照：rows={Rows} delivered={Delivered} ({Mode})",
                stream.Name,
                totalRows,
                totalDelivered,
                modeDesc);

            return new PgOutputSnapshotBatchResult(
                lsnText,
                totalRows,
                totalDelivered,
                usedExportedSnapshot,
                DateTime.UtcNow,
                $"已完成全量一致性基线快照（{modeDesc}）：读取 {totalRows} 行，投递 {totalDelivered} 项。");
        }
        finally
        {
            if (replConn != null)
            {
                await replConn.DisposeAsync();
            }
        }
    }

    private static async Task<IReadOnlyList<MonitoredTableSpec>> LoadPublicationTablesAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string publicationName,
        CancellationToken cancellationToken)
    {
        var specs = new List<MonitoredTableSpec>();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT pt.schemaname, pt.tablename
            FROM pg_publication_tables pt
            WHERE pt.pubname = @pub
            ORDER BY pt.schemaname, pt.tablename
            LIMIT @maxCount
            """,
            conn,
            tx);
        cmd.Parameters.AddWithValue("pub", publicationName);
        cmd.Parameters.AddWithValue("maxCount", CdcStreamConsts.MaxMonitoredTableCount);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            specs.Add(new MonitoredTableSpec(reader.GetString(0), reader.GetString(1)));
        }

        return specs;
    }

    private static string ValidateTablePart(string identifier)
        => CdcProtocolHelpers.ParseMonitoredTables($"{identifier}.{identifier}")[0].Schema;

    private static object? NormalizeValue(object value) => value switch
    {
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value,
    };
}
