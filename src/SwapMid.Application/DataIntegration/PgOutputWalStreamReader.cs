using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;
using NpgsqlTypes;

namespace SwapMid.DataIntegration;

internal sealed record PgOutputCapturedChange(
    string Schema,
    string Table,
    string Operation,
    string Lsn,
    DateTime CommitTimestampUtc,
    IReadOnlyDictionary<string, object?>? Data,
    IReadOnlyDictionary<string, object?>? OldData);

/// <summary>
/// 负责通过 <see cref="LogicalReplicationConnection"/> 有界流式读取 pgoutput WAL 已提交事务并物化变更记录。
/// </summary>
internal static class PgOutputWalStreamReader
{
    private const int ReplicationIdleTimeoutMs = 450;
    private const int ActiveTransactionIdleTimeoutMs = 1500;

    internal static NpgsqlLogSequenceNumber? DetermineStartLsn(
        string? checkpointLsn,
        string? confirmedFlushLsn)
    {
        var ckpt = CdcProtocolHelpers.TryParseLsn(checkpointLsn);
        var flush = CdcProtocolHelpers.TryParseLsn(confirmedFlushLsn);
        if (ckpt.HasValue && flush.HasValue)
        {
            return (ulong)ckpt.Value >= (ulong)flush.Value ? ckpt : flush;
        }

        return ckpt ?? flush;
    }

    internal static async Task<(List<PgOutputCapturedChange> Changes, NpgsqlLogSequenceNumber? LastCommittedLsn)> ReadCommittedChangesAsync(
        LogicalReplicationConnection replConn,
        PgOutputReplicationSlot slot,
        string publicationName,
        IReadOnlyList<MonitoredTableSpec> monitoredTables,
        int maxBatchSize,
        NpgsqlLogSequenceNumber? startLsn,
        NpgsqlLogSequenceNumber? headWalLsn,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var committed = new List<PgOutputCapturedChange>();
        var currentTxChanges = new List<PgOutputCapturedChange>();
        NpgsqlLogSequenceNumber? lastCommittedEndLsn = null;
        var inTransaction = false;
        var lastCommitTimestampUtc = DateTime.UtcNow;

        using var idleCts = new CancellationTokenSource();
        idleCts.CancelAfter(ReplicationIdleTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idleCts.Token);

        var options = new PgOutputReplicationOptions(publicationName, PgOutputProtocolVersion.V1);
        try
        {
            await foreach (var msg in replConn.StartReplication(slot, options, linkedCts.Token, startLsn))
            {
                switch (msg)
                {
                    case BeginMessage begin:
                        inTransaction = true;
                        currentTxChanges.Clear();
                        lastCommitTimestampUtc = ToUtc(begin.TransactionCommitTimestamp);
                        idleCts.CancelAfter(ActiveTransactionIdleTimeoutMs);
                        break;

                    case InsertMessage ins:
                        var insertData = await ReadTupleAsync(ins.NewRow, linkedCts.Token);
                        AppendIfMonitored(ins.Relation, "insert", ins.WalEnd, insertData, null, monitoredTables, lastCommitTimestampUtc, currentTxChanges);
                        idleCts.CancelAfter(inTransaction ? ActiveTransactionIdleTimeoutMs : ReplicationIdleTimeoutMs);
                        break;

                    case FullUpdateMessage fullUpd:
                        var oldFull = await ReadTupleAsync(fullUpd.OldRow, linkedCts.Token);
                        var newFull = await ReadTupleAsync(fullUpd.NewRow, linkedCts.Token);
                        AppendIfMonitored(fullUpd.Relation, "update", fullUpd.WalEnd, newFull, oldFull, monitoredTables, lastCommitTimestampUtc, currentTxChanges);
                        idleCts.CancelAfter(inTransaction ? ActiveTransactionIdleTimeoutMs : ReplicationIdleTimeoutMs);
                        break;

                    case IndexUpdateMessage idxUpd:
                        var keyIdx = await ReadTupleAsync(idxUpd.Key, linkedCts.Token);
                        var newIdx = await ReadTupleAsync(idxUpd.NewRow, linkedCts.Token);
                        AppendIfMonitored(idxUpd.Relation, "update", idxUpd.WalEnd, newIdx, keyIdx, monitoredTables, lastCommitTimestampUtc, currentTxChanges);
                        idleCts.CancelAfter(inTransaction ? ActiveTransactionIdleTimeoutMs : ReplicationIdleTimeoutMs);
                        break;

                    case UpdateMessage upd:
                        var newUpd = await ReadTupleAsync(upd.NewRow, linkedCts.Token);
                        AppendIfMonitored(upd.Relation, "update", upd.WalEnd, newUpd, null, monitoredTables, lastCommitTimestampUtc, currentTxChanges);
                        idleCts.CancelAfter(inTransaction ? ActiveTransactionIdleTimeoutMs : ReplicationIdleTimeoutMs);
                        break;

                    case KeyDeleteMessage keyDel:
                        var delKey = await ReadTupleAsync(keyDel.Key, linkedCts.Token);
                        AppendIfMonitored(keyDel.Relation, "delete", keyDel.WalEnd, null, delKey, monitoredTables, lastCommitTimestampUtc, currentTxChanges);
                        idleCts.CancelAfter(inTransaction ? ActiveTransactionIdleTimeoutMs : ReplicationIdleTimeoutMs);
                        break;

                    case FullDeleteMessage fullDel:
                        var delFull = await ReadTupleAsync(fullDel.OldRow, linkedCts.Token);
                        AppendIfMonitored(fullDel.Relation, "delete", fullDel.WalEnd, null, delFull, monitoredTables, lastCommitTimestampUtc, currentTxChanges);
                        idleCts.CancelAfter(inTransaction ? ActiveTransactionIdleTimeoutMs : ReplicationIdleTimeoutMs);
                        break;

                    case CommitMessage commit:
                        inTransaction = false;
                        lastCommittedEndLsn = commit.TransactionEndLsn;
                        var commitTs = ToUtc(commit.TransactionCommitTimestamp);
                        foreach (var item in currentTxChanges)
                        {
                            committed.Add(item with { CommitTimestampUtc = commitTs, Lsn = commit.TransactionEndLsn.ToString() });
                        }
                        currentTxChanges.Clear();

                        if (committed.Count >= maxBatchSize ||
                            (headWalLsn.HasValue && (ulong)commit.TransactionEndLsn >= (ulong)headWalLsn.Value && committed.Count > 0))
                        {
                            return (committed, lastCommittedEndLsn);
                        }

                        idleCts.CancelAfter(ReplicationIdleTimeoutMs);
                        break;

                    default:
                        idleCts.CancelAfter(inTransaction ? ActiveTransactionIdleTimeoutMs : ReplicationIdleTimeoutMs);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (idleCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("PostgreSQL pgoutput 增量流达到有界空闲窗口，结束本轮拉取（captured={Count}）。", committed.Count);
        }

        return (committed, lastCommittedEndLsn);
    }

    private static void AppendIfMonitored(
        RelationMessage relation,
        string operation,
        NpgsqlLogSequenceNumber walEnd,
        IReadOnlyDictionary<string, object?>? data,
        IReadOnlyDictionary<string, object?>? oldData,
        IReadOnlyList<MonitoredTableSpec> monitoredTables,
        DateTime commitTimestampUtc,
        List<PgOutputCapturedChange> target)
    {
        if (CdcProtocolHelpers.ShouldCaptureRelation(relation.Namespace, relation.RelationName, monitoredTables))
        {
            target.Add(new PgOutputCapturedChange(
                relation.Namespace,
                relation.RelationName,
                operation,
                walEnd.ToString(),
                commitTimestampUtc,
                data,
                oldData));
        }
    }

    private static DateTime ToUtc(DateTime timestamp)
        => timestamp.Kind == DateTimeKind.Utc
            ? timestamp
            : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

    private static async Task<IReadOnlyDictionary<string, object?>> ReadTupleAsync(
        ReplicationTuple tuple,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        await foreach (var column in tuple)
        {
            var fieldName = column.GetFieldName();
            if (column.IsDBNull)
            {
                values[fieldName] = null;
            }
            else if (column.IsUnchangedToastedValue)
            {
                values[fieldName] = "__toast_unchanged__";
            }
            else
            {
                values[fieldName] = await column.Get(cancellationToken);
            }
        }

        return values;
    }

    internal static async Task AdvanceSlotCheckpointAsync(
        NpgsqlConnection conn,
        string slotName,
        NpgsqlLogSequenceNumber flushLsn,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await using var checkCmd = new NpgsqlCommand(
                "SELECT active FROM pg_replication_slots WHERE slot_name = @slotName",
                conn);
            checkCmd.Parameters.AddWithValue("slotName", slotName);
            var activeObj = await checkCmd.ExecuteScalarAsync(cancellationToken);
            if (activeObj is not true)
            {
                break;
            }

            await Task.Delay(25, cancellationToken);
        }

        await using var cmd = new NpgsqlCommand(
            """
            SELECT (pg_replication_slot_advance(slot_name, CAST(@lsn AS pg_lsn))).end_lsn::text
            FROM pg_replication_slots
            WHERE slot_name = @slotName
              AND (confirmed_flush_lsn IS NULL OR confirmed_flush_lsn < CAST(@lsn AS pg_lsn));
            """,
            conn);
        cmd.Parameters.AddWithValue("slotName", slotName);
        cmd.Parameters.AddWithValue("lsn", flushLsn.ToString());
        await cmd.ExecuteScalarAsync(cancellationToken);
    }
}
