using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NpgsqlTypes;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 将 PostgreSQL pgoutput 捕获的已提交变更批次转换为标准 CDC 变更行集合，
/// 复用 <see cref="IEtlSinkWriter"/>（Kafka / ClickHouse / ObjectStorage）完成受 SSRF 守卫的流式投递。
/// </summary>
internal static class CdcExternalSinkDeliveryAdapter
{
    private static readonly string[] Columns =
    [
        "event_id",
        "stream_id",
        "stream_name",
        "source_schema",
        "source_table",
        "op",
        "lsn",
        "commit_timestamp_utc",
        "changed_columns",
        "after_json",
        "before_json",
    ];

    private static readonly Type[] ClrTypes =
    [
        typeof(string),
        typeof(string),
        typeof(string),
        typeof(string),
        typeof(string),
        typeof(string),
        typeof(string),
        typeof(DateTime),
        typeof(string),
        typeof(string),
        typeof(string),
    ];

    internal static async Task<long> DeliverAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        IReadOnlyList<PgOutputCapturedChange> changes,
        NpgsqlLogSequenceNumber? batchEndLsn,
        IReadOnlyDictionary<DataSourceType, IEtlSinkWriter> sinkWriters,
        CancellationToken cancellationToken)
    {
        if (targetSink.Type is not (DataSourceType.Kafka or DataSourceType.ClickHouse or DataSourceType.ObjectStorage))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"外部接收端仅支持 Kafka / ClickHouse / ObjectStorage，当前为 {targetSink.Type}。");
        }

        var destination = stream.TargetSinkDestination?.Trim();
        if (string.IsNullOrWhiteSpace(destination) ||
            !EtlTransformConfig.IsValidDestinationFor(targetSink.Type, destination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "外部接收端目标标识（Topic / 库表 / 对象前缀）缺失或格式不合法。");
        }

        if (!sinkWriters.TryGetValue(targetSink.Type, out var writer))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"未注册类型为 {targetSink.Type} 的外部接收端写入器。");
        }

        var transform = new EtlTransformConfig
        {
            TargetTable = destination,
            Mode = EtlWriteMode.Append,
        };

        var endLsnText = batchEndLsn?.ToString() ?? changes[^1].Lsn;
        var runId = CdcProtocolHelpers.BuildDeterministicPipelineRunId(stream.Id, endLsnText);
        var rows = BuildRows(stream, changes);
        var partitionKeys = BuildPartitionKeys(stream, changes);

        return await writer.WriteAsync(
            stream.Id,
            runId,
            targetSink,
            transform,
            async (consumeBatchAsync, ct) =>
            {
                var batch = new EtlSourceBatch(Columns, ClrTypes, rows, batchIndex: 0, partitionKeys: partitionKeys);
                await consumeBatchAsync(batch, ct);
            },
            cancellationToken);
    }

    internal static string?[]? BuildPartitionKeys(
        CdcStreamDefinition stream,
        IReadOnlyList<PgOutputCapturedChange> changes)
    {
        var keyColumns = CdcProtocolHelpers.ParseSinkKeyColumns(stream.TargetSinkKeyColumns);
        if (keyColumns.Count == 0)
        {
            return null;
        }

        var keys = new string?[changes.Count];
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            keys[i] = CdcProtocolHelpers.BuildPartitionKey(
                change.Schema,
                change.Table,
                keyColumns,
                change.Data,
                change.OldData);
        }

        return keys;
    }

    internal static List<object?[]> BuildRows(
        CdcStreamDefinition stream,
        IReadOnlyList<PgOutputCapturedChange> changes)
    {
        var rows = new List<object?[]>(changes.Count);
        var streamIdText = stream.Id.ToString("D");

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
            var changedCols = CdcProtocolHelpers.ComputeChangedColumns(change.Data, change.OldData);

            rows.Add(new object?[]
            {
                eventId.ToString("D"),
                streamIdText,
                stream.Name,
                change.Schema,
                change.Table,
                change.Operation,
                change.Lsn,
                change.CommitTimestampUtc.ToUniversalTime(),
                changedCols == null ? null : JsonSerializer.Serialize(changedCols),
                change.Data == null ? null : JsonSerializer.Serialize(change.Data),
                change.OldData == null ? null : JsonSerializer.Serialize(change.OldData),
            });
        }

        return rows;
    }
}
