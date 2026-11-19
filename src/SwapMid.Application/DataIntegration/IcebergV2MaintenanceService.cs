using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// Apache Iceberg v2 表生命周期维护引擎（纯 .NET，零 JVM）：
/// 1. 命名标签管理（WAP / 审计锚点 <c>refs[tag]</c>），受 <c>expireSnapshots</c> 永久保护；
/// 2. 安全快照过期（<c>expireSnapshots</c>）与严格引用计数孤儿 Parquet / Avro 清单回收（默认 <c>DryRun = true</c>）。
/// </summary>
public sealed partial class IcebergV2MaintenanceService : ITransientDependency
{
    private const int MaxOptimisticRetries = 16;
    private static readonly Regex TagNamePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public IcebergV2MaintenanceService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<CdcLakehouseTagResultDto> CreateTagAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        CreateCdcLakehouseTagInput input,
        CancellationToken cancellationToken = default)
    {
        EnsureIcebergStream(stream, targetSink);
        var tagName = (input.TagName ?? string.Empty).Trim();
        if (!TagNamePattern.IsMatch(tagName) || string.Equals(tagName, "main", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "标签名必须由字母/数字开头，仅含字母、数字、点、下划线与连字符（1-64 字符），且不可使用保留分支名 main。");
        }

        var config = ObjectStorageSinkConfigContract.ParseForWrite(targetSink.Config, requireSecretKey: true);
        var destination = stream.TargetSinkDestination!.Trim();

        for (var attempt = 0; attempt < MaxOptimisticRetries; attempt++)
        {
            var hintSeq = await IcebergV2CommitWriter.TryReadVersionHintAsync(
                _httpClientFactory, _configuration, config, destination, cancellationToken) ?? 0L;
            var (latestSeq, latestJson) = await IcebergV2CommitWriter.ProbeLatestMetadataAsync(
                _httpClientFactory, _configuration, config, destination, hintSeq, cancellationToken);

            if (latestSeq <= 0 || string.IsNullOrWhiteSpace(latestJson))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "目标 Apache Iceberg v2 表尚未产生任何快照，无法创建命名标签。");
            }

            var root = JsonNode.Parse(latestJson)?.AsObject()
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                    .WithData("Reason", "无法解析当前 Apache Iceberg v2 表元数据。");

            var snapshots = ExtractSnapshots(root);
            if (snapshots.Count == 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "当前 Apache Iceberg v2 表中不存在可用快照。");
            }

            var targetSnap = input.SnapshotVersion.HasValue
                ? snapshots.FirstOrDefault(s => s.SequenceNumber == input.SnapshotVersion.Value)
                : snapshots.OrderByDescending(s => s.SequenceNumber).First();
            if (targetSnap == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"指定的快照版本 v{input.SnapshotVersion} 不存在。");
            }

            var nowUtc = DateTime.UtcNow;
            var nowMs = new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds();
            var nextMetaSeq = latestSeq + 1;
            var prevMetaKey = config.BuildIcebergMetadataKey(destination, latestSeq);
            var nextMetaKey = config.BuildIcebergMetadataKey(destination, nextMetaSeq);

            AppendMetadataLog(root, config.Bucket, prevMetaKey, nowMs);
            root["last-updated-ms"] = nowMs;

            var refsObj = root["refs"] as JsonObject ?? new JsonObject();
            var tagNode = new JsonObject
            {
                ["snapshot-id"] = targetSnap.SnapshotId,
                ["type"] = "tag",
            };
            if (input.MaxRefAgeMs.HasValue)
            {
                tagNode["max-ref-age-ms"] = input.MaxRefAgeMs.Value;
            }

            refsObj[tagName] = tagNode;
            root["refs"] = refsObj;

            var updatedJson = root.ToJsonString(IcebergV2CommitWriter.JsonOptions);
            if (await IcebergV2CommitWriter.PutMetadataIfAbsentAsync(
                    _httpClientFactory, _configuration, config, nextMetaKey, updatedJson, cancellationToken))
            {
                await IcebergV2CommitWriter.PutVersionHintAsync(
                    _httpClientFactory, _configuration, config, destination, nextMetaSeq, cancellationToken);
                PlatformMetrics.CdcIcebergMaintenanceTotal.WithLabels("tag", "succeeded").Inc();
                return new CdcLakehouseTagResultDto
                {
                    StreamId = stream.Id,
                    TagName = tagName,
                    SnapshotId = targetSnap.SnapshotId,
                    SnapshotVersion = targetSnap.SequenceNumber,
                    MetadataVersion = nextMetaSeq,
                    MetadataKey = nextMetaKey,
                    MaxRefAgeMs = input.MaxRefAgeMs,
                    UpdatedAtUtc = nowUtc,
                    Message = $"已将 Apache Iceberg v2 标签「{tagName}」绑定到快照 v{targetSnap.SequenceNumber}（Snapshot {targetSnap.SnapshotId}，元数据 v{nextMetaSeq}）。",
                };
            }
        }

        PlatformMetrics.CdcIcebergMaintenanceTotal.WithLabels("tag", "failed").Inc();
        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", "Apache Iceberg v2 命名标签元数据并发提交冲突，请重试。");
    }

    public async Task<CdcLakehouseExpireResultDto> ExpireSnapshotsAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        ExpireCdcLakehouseSnapshotsInput input,
        CancellationToken cancellationToken = default)
    {
        EnsureIcebergStream(stream, targetSink);
        var retainLast = Math.Clamp(input.RetainLast, 1, 100);
        var olderThanMinutes = Math.Clamp(input.OlderThanMinutes, 0, 525_600);
        var config = ObjectStorageSinkConfigContract.ParseForWrite(targetSink.Config, requireSecretKey: true);
        var destination = stream.TargetSinkDestination!.Trim();

        for (var attempt = 0; attempt < MaxOptimisticRetries; attempt++)
        {
            var hintSeq = await IcebergV2CommitWriter.TryReadVersionHintAsync(
                _httpClientFactory, _configuration, config, destination, cancellationToken) ?? 0L;
            var (latestSeq, latestJson) = await IcebergV2CommitWriter.ProbeLatestMetadataAsync(
                _httpClientFactory, _configuration, config, destination, hintSeq, cancellationToken);

            if (latestSeq <= 0 || string.IsNullOrWhiteSpace(latestJson))
            {
                return new CdcLakehouseExpireResultDto
                {
                    StreamId = stream.Id,
                    DryRun = input.DryRun,
                    CurrentMetadataVersion = 0,
                    CompletedAtUtc = DateTime.UtcNow,
                    Summary = "目标表尚未产生 Apache Iceberg v2 快照，无需执行快照过期清理。",
                };
            }

            var root = JsonNode.Parse(latestJson)?.AsObject()
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                    .WithData("Reason", "无法解析当前 Apache Iceberg v2 表元数据。");

            var snapshots = ExtractSnapshots(root).OrderBy(s => s.SequenceNumber).ToList();
            var currentSnapId = root["current-snapshot-id"]?.GetValue<long>() ?? 0L;
            var pinnedRefSnapIds = ExtractReferencedSnapshotIds(root);
            var nowUtc = DateTime.UtcNow;
            var cutoffMs = new DateTimeOffset(nowUtc.AddMinutes(-olderThanMinutes)).ToUnixTimeMilliseconds();

            var latestProtectedIds = snapshots
                .OrderByDescending(s => s.SequenceNumber)
                .Take(retainLast)
                .Select(s => s.SnapshotId)
                .ToHashSet();

            var retained = new List<SnapshotMeta>();
            var expired = new List<SnapshotMeta>();
            var pinnedCount = 0;
            foreach (var snap in snapshots)
            {
                var isCurrent = snap.SnapshotId == currentSnapId;
                var inRetainLast = latestProtectedIds.Contains(snap.SnapshotId);
                var pinnedByRef = pinnedRefSnapIds.Contains(snap.SnapshotId);
                var newerThanCutoff = olderThanMinutes > 0 && snap.TimestampMs > cutoffMs;

                if (pinnedByRef && !isCurrent && !inRetainLast)
                {
                    pinnedCount++;
                }

                if (isCurrent || inRetainLast || pinnedByRef || newerThanCutoff)
                {
                    retained.Add(snap);
                }
                else
                {
                    expired.Add(snap);
                }
            }

            var (orphanObjects, reclaimableBytes) = await ComputeUnreferencedObjectsAsync(
                config, destination, retained, expired, cancellationToken);

            if (input.DryRun || expired.Count == 0)
            {
                var modeLabel = input.DryRun ? "Dry-Run 维护计划（零修改）" : "无需清理";
                return new CdcLakehouseExpireResultDto
                {
                    StreamId = stream.Id,
                    DryRun = input.DryRun,
                    CurrentMetadataVersion = latestSeq,
                    NewMetadataVersion = null,
                    RetainedSnapshotCount = retained.Count,
                    ExpiredSnapshotCount = expired.Count,
                    PinnedByRefCount = pinnedCount,
                    ExpiredVersions = expired.Select(s => s.SequenceNumber).ToList(),
                    CandidateOrphanFileCount = orphanObjects.Count,
                    DeletedOrphanFileCount = 0,
                    ReclaimableBytes = reclaimableBytes,
                    CompletedAtUtc = nowUtc,
                    Summary = $"{modeLabel}：可过期快照 {expired.Count} 个，保留 {retained.Count} 个（含标签锚定 {pinnedCount} 个），可回收孤儿文件 {orphanObjects.Count} 个（{reclaimableBytes} B）。",
                };
            }

            var retainedIds = retained.Select(s => s.SnapshotId).ToHashSet();
            var nowMs = new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds();
            var nextMetaSeq = latestSeq + 1;
            var prevMetaKey = config.BuildIcebergMetadataKey(destination, latestSeq);
            var nextMetaKey = config.BuildIcebergMetadataKey(destination, nextMetaSeq);

            AppendMetadataLog(root, config.Bucket, prevMetaKey, nowMs);
            root["last-updated-ms"] = nowMs;

            var newSnapsArray = new JsonArray();
            if (root["snapshots"] is JsonArray oldSnaps)
            {
                foreach (var node in oldSnaps)
                {
                    var sid = node?["snapshot-id"]?.GetValue<long>() ?? 0L;
                    if (retainedIds.Contains(sid) && node != null)
                    {
                        newSnapsArray.Add(node.DeepClone());
                    }
                }
            }

            root["snapshots"] = newSnapsArray;

            var newLogArray = new JsonArray();
            if (root["snapshot-log"] is JsonArray oldLog)
            {
                foreach (var node in oldLog)
                {
                    var sid = node?["snapshot-id"]?.GetValue<long>() ?? 0L;
                    if (retainedIds.Contains(sid) && node != null)
                    {
                        newLogArray.Add(node.DeepClone());
                    }
                }
            }

            root["snapshot-log"] = newLogArray;

            var updatedJson = root.ToJsonString(IcebergV2CommitWriter.JsonOptions);
            if (await IcebergV2CommitWriter.PutMetadataIfAbsentAsync(
                    _httpClientFactory, _configuration, config, nextMetaKey, updatedJson, cancellationToken))
            {
                await IcebergV2CommitWriter.PutVersionHintAsync(
                    _httpClientFactory, _configuration, config, destination, nextMetaSeq, cancellationToken);

                var deletedOrphans = 0;
                if (input.DeleteOrphanedFiles && orphanObjects.Count > 0)
                {
                    foreach (var orphanKey in orphanObjects)
                    {
                        if (await TryDeleteObjectAsync(config, orphanKey, cancellationToken))
                        {
                            deletedOrphans++;
                        }
                    }
                }

                PlatformMetrics.CdcIcebergMaintenanceTotal.WithLabels("expire", "succeeded").Inc();
                return new CdcLakehouseExpireResultDto
                {
                    StreamId = stream.Id,
                    DryRun = false,
                    CurrentMetadataVersion = latestSeq,
                    NewMetadataVersion = nextMetaSeq,
                    RetainedSnapshotCount = retained.Count,
                    ExpiredSnapshotCount = expired.Count,
                    PinnedByRefCount = pinnedCount,
                    ExpiredVersions = expired.Select(s => s.SequenceNumber).ToList(),
                    CandidateOrphanFileCount = orphanObjects.Count,
                    DeletedOrphanFileCount = deletedOrphans,
                    ReclaimableBytes = reclaimableBytes,
                    CompletedAtUtc = nowUtc,
                    Summary = $"已过期 {expired.Count} 个 Apache Iceberg v2 快照并推进元数据至 v{nextMetaSeq}（保留 {retained.Count} 个，标签保护 {pinnedCount} 个，物理清理孤儿对象 {deletedOrphans}/{orphanObjects.Count} 个）。",
                };
            }
        }

        PlatformMetrics.CdcIcebergMaintenanceTotal.WithLabels("expire", "failed").Inc();
        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", "Apache Iceberg v2 快照过期元数据并发提交冲突，请重试。");
    }

    private async Task<(List<string> OrphanKeys, long ReclaimableBytes)> ComputeUnreferencedObjectsAsync(
        ObjectStorageSinkConfig config,
        string destination,
        IReadOnlyList<SnapshotMeta> retained,
        IReadOnlyList<SnapshotMeta> expired,
        CancellationToken cancellationToken)
    {
        if (expired.Count == 0)
        {
            return (new List<string>(), 0L);
        }

        var retainedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snap in retained)
        {
            var (files, manifestKeys) = await CollectSnapshotObjectsAsync(config, destination, snap, cancellationToken);
            foreach (var f in files) retainedKeys.Add(f.ObjectKey);
            foreach (var mk in manifestKeys) retainedKeys.Add(mk);
        }

        var orphanSizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var snap in expired)
        {
            var (files, manifestKeys) = await CollectSnapshotObjectsAsync(config, destination, snap, cancellationToken);
            foreach (var f in files)
            {
                if (!retainedKeys.Contains(f.ObjectKey))
                {
                    orphanSizes.TryAdd(f.ObjectKey, f.FileSizeInBytes);
                }
            }

            foreach (var mk in manifestKeys)
            {
                if (!retainedKeys.Contains(mk))
                {
                    orphanSizes.TryAdd(mk, 0L);
                }
            }
        }

        var keys = orphanSizes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        return (keys, orphanSizes.Values.Sum());
    }

    private async Task<(List<IcebergV2CommitWriter.IcebergManifestEntry> Files, List<string> ManifestKeys)>
        CollectSnapshotObjectsAsync(
            ObjectStorageSinkConfig config,
            string destination,
            SnapshotMeta snap,
            CancellationToken cancellationToken)
    {
        var files = await IcebergV2CommitWriter.LoadSnapshotActiveFilesAsync(
            _httpClientFactory, _configuration, config, destination, snap.SnapshotId, snap.ManifestListUri, cancellationToken);
        var manifestKeys = new List<string>();
        if (!string.IsNullOrWhiteSpace(snap.ManifestListUri))
        {
            var mlKey = IcebergAvroManifestBuilder.StripS3BucketPrefix(snap.ManifestListUri, config.Bucket);
            if (!string.IsNullOrWhiteSpace(mlKey))
            {
                manifestKeys.Add(mlKey);
                if (mlKey.EndsWith(".avro", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await IcebergV2CommitWriter.TryGetObjectBytesAsync(
                        _httpClientFactory, _configuration, config, mlKey, cancellationToken);
                    if (bytes is { Length: > 0 })
                    {
                        foreach (var mf in IcebergAvroManifestBuilder.ReadManifestListAvro(bytes))
                        {
                            var mfKey = IcebergAvroManifestBuilder.StripS3BucketPrefix(mf.ManifestPath, config.Bucket);
                            if (!string.IsNullOrWhiteSpace(mfKey))
                            {
                                manifestKeys.Add(mfKey);
                            }
                        }
                    }
                }
            }
        }

        return (files, manifestKeys);
    }

    private async Task<bool> TryDeleteObjectAsync(
        ObjectStorageSinkConfig config,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var objectUri = config.BuildObjectUri(objectKey);
            await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, _configuration, cancellationToken);
            using var req = new HttpRequestMessage(HttpMethod.Delete, objectUri);
            AwsSigV4Signer.Sign(req, config.Region, config.AccessKey, config.SecretKey ?? string.Empty, AwsSigV4Signer.EmptyPayloadSha256Hex);
            var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EnsureIcebergStream(CdcStreamDefinition stream, DataSource targetSink)
    {
        if (stream.TargetMode != CdcTargetMode.ExternalSink ||
            targetSink.Type != DataSourceType.ObjectStorage ||
            stream.LakehouseTableFormat != LakehouseTableFormat.ApacheIceberg ||
            string.IsNullOrWhiteSpace(stream.TargetSinkDestination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅目标为对象存储（S3/MinIO）且表格式为 Apache Iceberg v2 的 CDC 流支持命名标签与快照过期维护。");
        }
    }

    private static List<SnapshotMeta> ExtractSnapshots(JsonObject root)
    {
        var list = new List<SnapshotMeta>();
        if (root["snapshots"] is not JsonArray snaps)
        {
            return list;
        }

        foreach (var node in snaps)
        {
            if (node is not JsonObject obj) continue;
            var seq = obj["sequence-number"]?.GetValue<long>() ?? 0L;
            var sid = obj["snapshot-id"]?.GetValue<long>() ?? 0L;
            var tsMs = obj["timestamp-ms"]?.GetValue<long>() ?? 0L;
            var mlUri = obj["manifest-list"]?.GetValue<string>();
            if (seq > 0 && sid > 0)
            {
                list.Add(new SnapshotMeta(seq, sid, tsMs, mlUri));
            }
        }

        return list;
    }

    private static HashSet<long> ExtractReferencedSnapshotIds(JsonObject root)
    {
        var set = new HashSet<long>();
        if (root["refs"] is not JsonObject refsObj)
        {
            return set;
        }

        foreach (var kv in refsObj)
        {
            var sid = kv.Value?["snapshot-id"]?.GetValue<long>() ?? 0L;
            if (sid > 0)
            {
                set.Add(sid);
            }
        }

        return set;
    }

    private static void AppendMetadataLog(JsonObject root, string bucket, string prevMetadataKey, long fallbackMs)
    {
        var log = root["metadata-log"] as JsonArray ?? new JsonArray();
        var prevUpdated = root["last-updated-ms"]?.GetValue<long>() ?? fallbackMs;
        log.Add(new JsonObject
        {
            ["timestamp-ms"] = prevUpdated,
            ["metadata-file"] = $"s3://{bucket}/{prevMetadataKey}",
        });
        root["metadata-log"] = log;
    }

    private sealed record SnapshotMeta(long SequenceNumber, long SnapshotId, long TimestampMs, string? ManifestListUri);
}
