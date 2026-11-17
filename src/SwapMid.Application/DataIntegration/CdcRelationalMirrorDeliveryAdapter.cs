using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 将 PostgreSQL pgoutput 捕获的已提交变更（及 EXPORT_SNAPSHOT 基线快照）
/// 按业务主键折叠并原子物化（Upsert / Delete）至下游关系型数据库目标表。
/// </summary>
internal static class CdcRelationalMirrorDeliveryAdapter
{
    private const string ToastUnchangedSentinel = "__toast_unchanged__";
    private const int ConnectTimeoutSeconds = 15;
    private const int CommandTimeoutSeconds = 60;
    private const int CleanupTimeoutSeconds = 10;
    private const int StagingBatchSize = 500;
    private const int TargetTransferBatchSize = 5_000;

    internal sealed record CoalescedRow(
        bool IsDelete,
        Dictionary<string, object?>? Data,
        object?[] KeyValues);

    internal static async Task<long> DeliverAsync(
        CdcStreamDefinition stream,
        DataSource targetDatabase,
        IReadOnlyList<PgOutputCapturedChange> changes,
        IEtlRelationalConnectionFactory connectionFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return 0;
        }

        if (targetDatabase.Type != DataSourceType.RelationalDatabase)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"关系表行级镜像模式仅支持关系型数据库目标，当前为 {targetDatabase.Type}。");
        }

        var monitoredTables = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables);
        if (monitoredTables.Count != 1)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "关系表行级镜像（RelationalTable）模式下必须在监听表清单中明确指定唯一 1 张源表。");
        }

        var destination = stream.TargetSinkDestination?.Trim();
        if (string.IsNullOrWhiteSpace(destination) ||
            !EtlTransformConfig.IsValidDestinationFor(DataSourceType.RelationalDatabase, destination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "关系型目标表名缺失或格式不合法（示例：public.orders_mirror）。");
        }

        var requestedKeyColumns = CdcProtocolHelpers.ParseSinkKeyColumns(stream.TargetSinkKeyColumns);
        if (requestedKeyColumns.Count == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "关系表行级镜像模式下必须配置至少一个业务主键列（TargetSinkKeyColumns）。");
        }

        RelationalDataSourceConnection.Probe targetProbe;
        try
        {
            targetProbe = RelationalDataSourceConnection.Parse(targetDatabase.Config);
        }
        catch (JsonException)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "目标关系型数据源连接配置 JSON 无法解析。");
        }
        catch (InvalidOperationException)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "目标关系型数据源连接配置不完整。");
        }

        if (targetProbe.NormalizedProvider is not (
            "postgresql" or "postgres" or "npgsql" or
            "sqlserver" or "mssql" or
            "mysql" or "mariadb" or "sqlite"))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"暂不支持向 {targetProbe.NormalizedProvider} 目标执行 CDC 关系表行级镜像。");
        }

        var coalesced = CoalesceChangesByKey(changes, requestedKeyColumns, stream.TargetDeletePolicy);
        var upsertRows = coalesced.Where(x => !x.IsDelete).ToList();
        var deleteEntries = coalesced.Where(x => x.IsDelete).ToList();
        if (upsertRows.Count == 0 && deleteEntries.Count == 0)
        {
            return 0;
        }

        var targetCleanupState = new EtlTargetConnectionCleanupState();
        await using var targetConnectionLease = new EtlTargetConnectionLease(
            connectionFactory.CreateTarget(targetDatabase.Config, ConnectTimeoutSeconds, CommandTimeoutSeconds),
            targetCleanupState,
            CleanupTimeoutSeconds,
            logger);
        var tgtConn = targetConnectionLease.Connection;
        await tgtConn.OpenAsync(cancellationToken);
        await EtlProviderSession.ConfigureAsync(tgtConn, targetProbe, CommandTimeoutSeconds, cancellationToken);

        var canonicalTarget = await EtlTargetSession.ResolveCanonicalTargetTableAsync(
            tgtConn, destination, targetProbe, CommandTimeoutSeconds, cancellationToken);
        var targetSchema = await EtlTargetSchemaInspector.InspectTargetTableAsync(
            tgtConn, targetProbe.NormalizedProvider, canonicalTarget, cancellationToken);
        if (targetSchema.Count == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"目标表 {canonicalTarget} 不存在或无法读取列定义。");
        }

        var targetColByName = targetSchema.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var canonicalKeyColumns = ResolveCanonicalKeyColumns(requestedKeyColumns, targetColByName);

        string[] projectedUpsertCols = Array.Empty<string>();
        EtlTargetUpsertPreflight.ResolvedUpsertPlan? upsertPlan = null;
        if (upsertRows.Count > 0)
        {
            projectedUpsertCols = ResolveUpsertColumns(targetSchema, upsertRows[0].Data!);
            upsertPlan = EtlTargetUpsertPreflight.ResolveProjection(
                projectedUpsertCols,
                new EtlUpsertSpec(canonicalKeyColumns, WatermarkColumn: null));
        }

        var preflightColumns = upsertPlan != null ? projectedUpsertCols : canonicalKeyColumns;
        var upsertSpecForPreflight = upsertPlan != null
            ? new EtlUpsertSpec(upsertPlan.KeyColumns, null)
            : null;
        var targetPreparation = await EtlTargetPreflight.PrepareAsync(
            tgtConn,
            canonicalTarget,
            preflightColumns,
            targetProbe,
            truncate: false,
            maxBatchPayloadBytes: EtlPipelineRunner.MaxBatchPayloadBytes,
            commandTimeoutSeconds: CommandTimeoutSeconds,
            ct: cancellationToken,
            upsertSpec: upsertSpecForPreflight);

        if (upsertSpecForPreflight == null)
        {
            await EtlTargetUpsertPreflight.ValidateTargetAsync(
                tgtConn,
                transaction: null,
                targetPreparation.CanonicalTargetTable,
                targetProbe,
                new EtlUpsertSpec(canonicalKeyColumns, null),
                CommandTimeoutSeconds,
                cancellationToken);
        }

        var deleteKeyValues = BuildDeleteKeyTuples(deleteEntries, canonicalKeyColumns, targetColByName);
        EtlTargetStagingWriter.Stage? stage = null;
        try
        {
            if (upsertRows.Count > 0)
            {
                stage = await EtlTargetStagingWriter.CreateAsync(
                    tgtConn,
                    targetPreparation.CanonicalTargetTable,
                    projectedUpsertCols,
                    targetProbe,
                    CommandTimeoutSeconds,
                    cancellationToken);

                var projectedBatch = BuildProjectedUpsertRows(upsertRows, projectedUpsertCols, targetColByName);
                long nextRowId = 1;
                for (var i = 0; i < projectedBatch.Count; i += StagingBatchSize)
                {
                    var count = Math.Min(StagingBatchSize, projectedBatch.Count - i);
                    var slice = projectedBatch.GetRange(i, count);
                    await EtlTargetStagingWriter.InsertBatchAsync(
                        tgtConn, stage, nextRowId, projectedUpsertCols, slice, targetProbe, CommandTimeoutSeconds, cancellationToken);
                    nextRowId += count;
                }

                await EtlTargetUpsertWriter.VerifyStageKeysAsync(
                    tgtConn, stage, upsertPlan!, targetProbe, CommandTimeoutSeconds, cancellationToken);
            }

            var (upserted, deleted) = await EtlTargetAtomicWriter.ApplyCdcMirrorAsync(
                tgtConn,
                stage,
                targetPreparation.CanonicalTargetTable,
                projectedUpsertCols,
                upsertRows.Count,
                upsertPlan,
                canonicalKeyColumns,
                deleteKeyValues,
                TargetTransferBatchSize,
                targetPreparation.SchemaFingerprint,
                targetProbe,
                CommandTimeoutSeconds,
                CleanupTimeoutSeconds,
                logger,
                targetCleanupState,
                cancellationToken);

            return upserted + deleted;
        }
        finally
        {
            if (stage != null)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
                    await EtlTargetStagingWriter.DropAsync(
                        tgtConn, stage, targetProbe, CommandTimeoutSeconds, cleanup.Token);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogWarning(
                        "CDC 关系表镜像暂存表清理失败 exceptionType={ExceptionType}",
                        cleanupEx.GetType().FullName ?? cleanupEx.GetType().Name);
                }
            }
        }
    }

    internal static List<CoalescedRow> CoalesceChangesByKey(
        IReadOnlyList<PgOutputCapturedChange> changes,
        IReadOnlyList<string> keyColumns,
        CdcDeletePolicy deletePolicy)
    {
        var order = new List<string>(changes.Count);
        var map = new Dictionary<string, CoalescedRow>(StringComparer.Ordinal);

        foreach (var change in changes)
        {
            var isDelete = string.Equals(change.Operation, "delete", StringComparison.OrdinalIgnoreCase);
            if (isDelete && deletePolicy == CdcDeletePolicy.Ignore)
            {
                continue;
            }

            var sourceDict = isDelete
                ? (change.OldData ?? change.Data)
                : (change.Data ?? change.OldData);
            if (sourceDict == null || sourceDict.Count == 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "CDC 变更载荷为空，无法提取业务主键进行行级物化。");
            }

            var lookup = new Dictionary<string, object?>(sourceDict, StringComparer.OrdinalIgnoreCase);
            var keyValues = ExtractKeyValues(lookup, keyColumns);
            var compositeKey = BuildCompositeKeyString(keyValues);

            if (isDelete)
            {
                if (!map.ContainsKey(compositeKey))
                {
                    order.Add(compositeKey);
                }

                map[compositeKey] = new CoalescedRow(IsDelete: true, Data: null, KeyValues: keyValues);
                continue;
            }

            var normalized = new Dictionary<string, object?>(lookup, StringComparer.OrdinalIgnoreCase);
            map.TryGetValue(compositeKey, out var previous);
            ResolveToastSentinels(normalized, previous, change.OldData);

            if (!map.ContainsKey(compositeKey))
            {
                order.Add(compositeKey);
            }

            map[compositeKey] = new CoalescedRow(IsDelete: false, Data: normalized, KeyValues: keyValues);
        }

        var result = new List<CoalescedRow>(order.Count);
        foreach (var key in order)
        {
            result.Add(map[key]);
        }

        return result;
    }

    private static void ResolveToastSentinels(
        Dictionary<string, object?> current,
        CoalescedRow? previous,
        IReadOnlyDictionary<string, object?>? oldData)
    {
        var oldLookup = oldData == null
            ? null
            : new Dictionary<string, object?>(oldData, StringComparer.OrdinalIgnoreCase);

        foreach (var colName in current.Keys.ToArray())
        {
            if (current[colName] is string s && string.Equals(s, ToastUnchangedSentinel, StringComparison.Ordinal))
            {
                if (oldLookup != null && oldLookup.TryGetValue(colName, out var oldVal) &&
                    !(oldVal is string os && string.Equals(os, ToastUnchangedSentinel, StringComparison.Ordinal)))
                {
                    current[colName] = oldVal;
                }
                else if (previous is { IsDelete: false, Data: not null } &&
                         previous.Data.TryGetValue(colName, out var prevVal))
                {
                    current[colName] = prevVal;
                }
            }
        }
    }

    private static object?[] ExtractKeyValues(
        IReadOnlyDictionary<string, object?> rowLookup,
        IReadOnlyList<string> keyColumns)
    {
        var values = new object?[keyColumns.Count];
        for (var i = 0; i < keyColumns.Count; i++)
        {
            if (!rowLookup.TryGetValue(keyColumns[i], out var value) || value == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"CDC 变更事件缺少非空业务主键列 \"{keyColumns[i]}\"。");
            }

            values[i] = value;
        }

        return values;
    }

    private static string BuildCompositeKeyString(object?[] keyValues)
    {
        var parts = new string[keyValues.Length];
        for (var i = 0; i < keyValues.Length; i++)
        {
            parts[i] = keyValues[i] switch
            {
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => keyValues[i]?.ToString() ?? string.Empty,
            };
        }

        return string.Join('\u001f', parts);
    }

    private static string[] ResolveCanonicalKeyColumns(
        IReadOnlyList<string> requestedKeys,
        IReadOnlyDictionary<string, PreflightColumnDto> targetColByName)
    {
        var canonical = new string[requestedKeys.Count];
        for (var i = 0; i < requestedKeys.Count; i++)
        {
            if (!targetColByName.TryGetValue(requestedKeys[i], out var col))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"目标表中不存在业务主键列 \"{requestedKeys[i]}\"。");
            }

            canonical[i] = col.Name;
        }

        return canonical;
    }

    private static string[] ResolveUpsertColumns(
        IReadOnlyList<PreflightColumnDto> targetSchema,
        IReadOnlyDictionary<string, object?> sampleRow)
    {
        var projected = targetSchema
            .Where(tc => sampleRow.ContainsKey(tc.Name))
            .Select(tc => tc.Name)
            .ToArray();

        if (projected.Length == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "源表捕获列与目标关系表列无交集，无法物化写入。");
        }

        return projected;
    }

    private static List<object?[]> BuildProjectedUpsertRows(
        IReadOnlyList<CoalescedRow> upsertRows,
        IReadOnlyList<string> projectedCols,
        IReadOnlyDictionary<string, PreflightColumnDto> targetColByName)
    {
        var list = new List<object?[]>(upsertRows.Count);
        foreach (var entry in upsertRows)
        {
            var data = entry.Data!;
            var row = new object?[projectedCols.Count];
            for (var c = 0; c < projectedCols.Count; c++)
            {
                var colName = projectedCols[c];
                data.TryGetValue(colName, out var rawVal);
                if (rawVal is string str && string.Equals(str, ToastUnchangedSentinel, StringComparison.Ordinal))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"UPDATE 事件中大字段列 \"{colName}\" 未包含新值，请对上游源表启用 REPLICA IDENTITY FULL。");
                }

                row[c] = CdcMirrorValueCoercer.Coerce(rawVal, targetColByName[colName].DataType);
            }

            list.Add(row);
        }

        return list;
    }

    private static List<object?[]> BuildDeleteKeyTuples(
        IReadOnlyList<CoalescedRow> deleteEntries,
        IReadOnlyList<string> canonicalKeyColumns,
        IReadOnlyDictionary<string, PreflightColumnDto> targetColByName)
    {
        var tuples = new List<object?[]>(deleteEntries.Count);
        foreach (var entry in deleteEntries)
        {
            var coerced = new object?[canonicalKeyColumns.Count];
            for (var k = 0; k < canonicalKeyColumns.Count; k++)
            {
                coerced[k] = CdcMirrorValueCoercer.Coerce(entry.KeyValues[k], targetColByName[canonicalKeyColumns[k]].DataType);
            }

            tuples.Add(coerced);
        }

        return tuples;
    }
}
