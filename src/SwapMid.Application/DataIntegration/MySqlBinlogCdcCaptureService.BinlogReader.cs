using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySqlCdc;
using MySqlCdc.Events;
using MySqlConnector;
using Volo.Abp;
using CdcSslMode = MySqlCdc.Constants.SslMode;

namespace SwapMid.DataIntegration;

public sealed partial class MySqlBinlogCdcCaptureService
{
    private static readonly RelationalDataSourceConnection.Probe MySqlQuoteProbe = new()
    {
        Provider = "mysql",
    };

    private static async Task<(List<PgOutputCapturedChange> Changes, MySqlBinlogCheckpoint EndCheckpoint)> ReadBinlogBatchAsync(
        RelationalDataSourceConnection.Probe probe,
        CdcStreamDefinition stream,
        IReadOnlyList<MonitoredTableSpec> specs,
        Dictionary<string, MySqlTableMetadata> metadata,
        MySqlBinlogCheckpoint startCheckpoint,
        MySqlBinlogCheckpoint activeServerCheckpoint,
        CancellationToken cancellationToken)
    {
        var serverId = 100_000L + (Math.Abs((long)stream.Id.GetHashCode()) % 1_900_000_000L);
        var sslMode = probe.Encrypt
            ? (probe.TrustServerCertificate ? CdcSslMode.Require : CdcSslMode.RequireVerifyFull)
            : CdcSslMode.IfAvailable;

        var client = new BinlogClient(options =>
        {
            options.Hostname = probe.Host ?? "127.0.0.1";
            options.Port = probe.Port > 0 ? probe.Port : 3306;
            options.Username = probe.Username ?? string.Empty;
            options.Password = probe.Password ?? string.Empty;
            options.Database = probe.Database ?? string.Empty;
            options.SslMode = sslMode;
            options.ServerId = serverId;
            options.Blocking = false;
            options.Binlog = BinlogOptions.FromPosition(startCheckpoint.Filename, startCheckpoint.Position);
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(BinlogReadTimeoutSeconds));

        var tableMap = new Dictionary<long, MySqlTableMetadata>();
        var committed = new List<PgOutputCapturedChange>();
        var pendingTx = new List<PgOutputCapturedChange>();
        var inTx = false;
        var currentFile = startCheckpoint.Filename;
        var committedCheckpoint = startCheckpoint;

        try
        {
            await foreach (var (header, binlogEvent) in client.Replicate(timeoutCts.Token))
            {
                if (binlogEvent is RotateEvent rotate)
                {
                    if (!string.IsNullOrWhiteSpace(rotate.BinlogFilename))
                    {
                        currentFile = rotate.BinlogFilename;
                        if (!inTx && rotate.BinlogPosition >= 4)
                        {
                            committedCheckpoint = new MySqlBinlogCheckpoint(currentFile, rotate.BinlogPosition);
                        }
                    }

                    continue;
                }

                if (binlogEvent is TableMapEvent tme)
                {
                    if (CdcProtocolHelpers.ShouldCaptureRelation(tme.DatabaseName, tme.TableName, specs))
                    {
                        var full = $"{tme.DatabaseName}.{tme.TableName}";
                        if (metadata.TryGetValue(full, out var meta))
                        {
                            tableMap[tme.TableId] = meta;
                        }
                    }
                    else
                    {
                        tableMap.Remove(tme.TableId);
                    }

                    continue;
                }

                var commitTs = header.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(header.Timestamp).UtcDateTime
                    : DateTime.UtcNow;

                if (binlogEvent is QueryEvent qe)
                {
                    var sql = qe.SqlStatement.Trim();
                    if (string.Equals(sql, "BEGIN", StringComparison.OrdinalIgnoreCase))
                    {
                        inTx = true;
                        pendingTx.Clear();
                    }
                    else if (string.Equals(sql, "COMMIT", StringComparison.OrdinalIgnoreCase))
                    {
                        CommitPendingTx();
                        if (committed.Count >= stream.MaxBatchSize || IsAtOrPastServerEnd())
                        {
                            break;
                        }
                    }
                    else if (string.Equals(sql, "ROLLBACK", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingTx.Clear();
                        inTx = false;
                        if (header.NextEventPosition >= 4)
                        {
                            committedCheckpoint = new MySqlBinlogCheckpoint(currentFile, header.NextEventPosition);
                        }
                    }

                    continue;
                }

                if (binlogEvent is XidEvent)
                {
                    CommitPendingTx();
                    if (committed.Count >= stream.MaxBatchSize || IsAtOrPastServerEnd())
                    {
                        break;
                    }

                    continue;
                }

                if (binlogEvent is WriteRowsEvent wre && tableMap.TryGetValue(wre.TableId, out var wMeta))
                {
                    var posToken = $"{currentFile}:{Math.Max(header.NextEventPosition, startCheckpoint.Position)}";
                    foreach (var row in wre.Rows)
                    {
                        var after = MapRowCells(row.Cells, wre.ColumnsPresent, wMeta.Columns);
                        pendingTx.Add(new PgOutputCapturedChange(
                            wMeta.Database, wMeta.Table, "insert", posToken, commitTs, after, null));
                    }

                    if (!inTx)
                    {
                        CommitPendingTx();
                    }
                }
                else if (binlogEvent is UpdateRowsEvent ure && tableMap.TryGetValue(ure.TableId, out var uMeta))
                {
                    var posToken = $"{currentFile}:{Math.Max(header.NextEventPosition, startCheckpoint.Position)}";
                    foreach (var pair in ure.Rows)
                    {
                        var before = MapRowCells(pair.BeforeUpdate.Cells, ure.ColumnsPresentBeforeUpdate, uMeta.Columns);
                        var after = MapRowCells(pair.AfterUpdate.Cells, ure.ColumnsPresentAfterUpdate, uMeta.Columns);
                        pendingTx.Add(new PgOutputCapturedChange(
                            uMeta.Database, uMeta.Table, "update", posToken, commitTs, after, before));
                    }

                    if (!inTx)
                    {
                        CommitPendingTx();
                    }
                }
                else if (binlogEvent is DeleteRowsEvent dre && tableMap.TryGetValue(dre.TableId, out var dMeta))
                {
                    var posToken = $"{currentFile}:{Math.Max(header.NextEventPosition, startCheckpoint.Position)}";
                    foreach (var row in dre.Rows)
                    {
                        var before = MapRowCells(row.Cells, dre.ColumnsPresent, dMeta.Columns);
                        pendingTx.Add(new PgOutputCapturedChange(
                            dMeta.Database, dMeta.Table, "delete", posToken, commitTs, null, before));
                    }

                    if (!inTx)
                    {
                        CommitPendingTx();
                    }
                }
                else if (!inTx && header.NextEventPosition >= 4 && header.EventType != MySqlCdc.Constants.EventType.FormatDescriptionEvent)
                {
                    committedCheckpoint = new MySqlBinlogCheckpoint(currentFile, header.NextEventPosition);
                }

                if (!inTx && (committed.Count >= stream.MaxBatchSize || IsAtOrPastServerEnd()))
                {
                    break;
                }

                void CommitPendingTx()
                {
                    if (pendingTx.Count > 0)
                    {
                        committed.AddRange(pendingTx);
                        pendingTx.Clear();
                    }

                    inTx = false;
                    if (header.NextEventPosition >= 4)
                    {
                        committedCheckpoint = new MySqlBinlogCheckpoint(currentFile, header.NextEventPosition);
                    }
                }

                bool IsAtOrPastServerEnd()
                    => string.Equals(currentFile, activeServerCheckpoint.Filename, StringComparison.Ordinal) &&
                       header.NextEventPosition >= activeServerCheckpoint.Position;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            // Bounded read window elapsed cleanly after collecting committed transactions.
        }

        return (committed, committedCheckpoint);
    }

    private static async Task<MySqlBinlogServerState> EnsureReadyAsync(
        MySqlConnection conn,
        IReadOnlyList<MonitoredTableSpec> tables,
        string? checkpoint,
        bool checkExpired,
        CancellationToken cancellationToken)
    {
        var state = await InspectServerAsync(conn, tables, checkpoint, cancellationToken);
        if (!state.LogBinEnabled ||
            !string.Equals(state.BinlogFormat, "ROW", StringComparison.OrdinalIgnoreCase) ||
            state.InvalidTables > 0 ||
            (checkExpired && state.CheckpointExpired))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                .WithData("Reason", string.Join("；", state.Diagnostics));
        }

        return state;
    }

    private static async Task ExecuteNonQueryAsync(MySqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string QuoteMySql(string identifier)
        => RelationalDataSourceConnection.QuoteIdentifierPart(identifier, MySqlQuoteProbe);
}
