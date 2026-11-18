using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// 将 PostgreSQL pgoutput 捕获的已提交变更（及 EXPORT_SNAPSHOT 基线快照）
/// 按业务主键折叠并原子物化（Upsert / Delete / SoftDelete）至下游关系型数据库目标表。
/// </summary>
internal static class CdcRelationalMirrorDeliveryAdapter
{
    private const int ConnectTimeoutSeconds = 15;
    private const int CommandTimeoutSeconds = 60;
    private const int CleanupTimeoutSeconds = 10;
    private const int StagingBatchSize = 500;
    private const int TargetTransferBatchSize = 5_000;

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

        var fieldMappings = CdcStreamMirrorConfigValidator.ParseFieldMappings(stream.TargetFieldMappingsJson);
        CdcStreamMirrorConfigValidator.ValidateMirrorColumns(
            stream.TargetMode,
            stream.TargetSinkKeyColumns,
            stream.TargetFieldMappingsJson,
            stream.TargetDeletePolicy,
            stream.SoftDeleteColumn,
            stream.SoftDeleteTimestampColumn);

        var targetProbe = ParseTargetProbe(targetDatabase);

        var coalesced = CdcRelationalMirrorRowProjector.CoalesceChangesByKey(
            changes,
            requestedKeyColumns,
            stream.TargetDeletePolicy,
            fieldMappings);
        var upsertRows = coalesced.Where(x => !x.IsDelete || x.HasFullPayload).ToList();
        var deleteEntries = stream.TargetDeletePolicy == CdcDeletePolicy.DeleteRow
            ? coalesced.Where(x => x.IsDelete && !x.HasFullPayload).ToList()
            : new List<CdcRelationalMirrorRowProjector.CoalescedRow>();
        var softDeleteUpdateEntries = stream.TargetDeletePolicy == CdcDeletePolicy.SoftDelete
            ? coalesced.Where(x => x.IsDelete && !x.HasFullPayload).ToList()
            : new List<CdcRelationalMirrorRowProjector.CoalescedRow>();

        if (upsertRows.Count == 0 && deleteEntries.Count == 0 && softDeleteUpdateEntries.Count == 0)
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
        var (canonicalSoftDeleteCol, canonicalSoftDeleteTsCol) = ResolveCanonicalSoftDeleteColumns(
            stream,
            targetColByName);

        string[] projectedUpsertCols = Array.Empty<string>();
        EtlTargetUpsertPreflight.ResolvedUpsertPlan? upsertPlan = null;
        if (upsertRows.Count > 0)
        {
            projectedUpsertCols = CdcRelationalMirrorRowProjector.ResolveUpsertColumns(
                targetSchema,
                upsertRows[0].Data!,
                fieldMappings,
                stream.TargetDeletePolicy,
                canonicalSoftDeleteCol,
                canonicalSoftDeleteTsCol);
            upsertPlan = EtlTargetUpsertPreflight.ResolveProjection(
                projectedUpsertCols,
                new EtlUpsertSpec(canonicalKeyColumns, WatermarkColumn: null));
        }

        var preflightColumns = BuildPreflightColumns(
            upsertPlan,
            projectedUpsertCols,
            canonicalKeyColumns,
            canonicalSoftDeleteCol,
            canonicalSoftDeleteTsCol);
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

        var deleteKeyValues = CdcRelationalMirrorRowProjector.BuildDeleteKeyTuples(
            deleteEntries,
            canonicalKeyColumns,
            targetColByName);
        EtlTargetAtomicWriter.CdcMirrorSoftDeleteSpec? softDeleteSpec = null;
        List<EtlTargetAtomicWriter.CdcMirrorSoftDeleteTuple>? softDeleteTuples = null;
        if (softDeleteUpdateEntries.Count > 0 && canonicalSoftDeleteCol != null)
        {
            softDeleteSpec = new EtlTargetAtomicWriter.CdcMirrorSoftDeleteSpec(
                canonicalSoftDeleteCol,
                canonicalSoftDeleteTsCol);
            softDeleteTuples = CdcRelationalMirrorRowProjector.BuildSoftDeleteTuples(
                softDeleteUpdateEntries,
                canonicalKeyColumns,
                targetColByName,
                canonicalSoftDeleteCol,
                canonicalSoftDeleteTsCol,
                targetProbe.NormalizedProvider);
        }

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

                var projectedBatch = CdcRelationalMirrorRowProjector.BuildProjectedUpsertRows(
                    upsertRows,
                    projectedUpsertCols,
                    targetColByName,
                    stream.TargetDeletePolicy,
                    canonicalSoftDeleteCol,
                    canonicalSoftDeleteTsCol,
                    targetProbe.NormalizedProvider);
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
                cancellationToken,
                softDeleteSpec,
                softDeleteTuples);

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

    private static RelationalDataSourceConnection.Probe ParseTargetProbe(DataSource targetDatabase)
    {
        RelationalDataSourceConnection.Probe targetProbe;
        try
        {
            targetProbe = RelationalDataSourceConnection.Parse(targetDatabase.Config);
        }
        catch (System.Text.Json.JsonException)
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

        return targetProbe;
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

    private static (string? SoftDeleteCol, string? SoftDeleteTsCol) ResolveCanonicalSoftDeleteColumns(
        CdcStreamDefinition stream,
        IReadOnlyDictionary<string, PreflightColumnDto> targetColByName)
    {
        if (stream.TargetDeletePolicy != CdcDeletePolicy.SoftDelete)
        {
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(stream.SoftDeleteColumn) ||
            !targetColByName.TryGetValue(stream.SoftDeleteColumn, out var flagCol))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"目标表中不存在软删除标记列 \"{stream.SoftDeleteColumn}\"。");
        }

        string? tsColName = null;
        if (!string.IsNullOrWhiteSpace(stream.SoftDeleteTimestampColumn))
        {
            if (!targetColByName.TryGetValue(stream.SoftDeleteTimestampColumn, out var tsCol))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"目标表中不存在软删除时间戳列 \"{stream.SoftDeleteTimestampColumn}\"。");
            }

            if (!tsCol.IsNullable)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"软删除时间戳列 \"{tsCol.Name}\" 必须允许 NULL，以便有效写入行或复活恢复时清空删除时间。");
            }

            tsColName = tsCol.Name;
        }

        return (flagCol.Name, tsColName);
    }

    private static IReadOnlyList<string> BuildPreflightColumns(
        EtlTargetUpsertPreflight.ResolvedUpsertPlan? upsertPlan,
        string[] projectedUpsertCols,
        string[] canonicalKeyColumns,
        string? canonicalSoftDeleteCol,
        string? canonicalSoftDeleteTsCol)
    {
        if (upsertPlan != null)
        {
            return projectedUpsertCols;
        }

        var cols = new List<string>(canonicalKeyColumns);
        if (!string.IsNullOrWhiteSpace(canonicalSoftDeleteCol))
        {
            cols.Add(canonicalSoftDeleteCol);
        }

        if (!string.IsNullOrWhiteSpace(canonicalSoftDeleteTsCol))
        {
            cols.Add(canonicalSoftDeleteTsCol);
        }

        return cols;
    }
}
