using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Monitoring;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public sealed partial class IcebergV2MaintenanceService
{
    /// <summary>
    /// Apache Iceberg v2 WAP 分支快进发布（Fast-Forward Promote）：
    /// 1. 强校验数据契约 SLA 门禁（默认 <c>RequireContractPass = true</c>）；
    /// 2. 强校验 <c>main</c> 分支是源分支（如 <c>staging</c>）的祖先，杜绝非快进覆盖；
    /// 3. 通过 S3 <c>If-None-Match: *</c> 原子推进 <c>current-snapshot-id</c>、<c>refs["main"]</c> 及可选审计标签。
    /// </summary>
    public async Task<CdcLakehouseBranchPromoteResultDto> PromoteBranchAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        PromoteCdcLakehouseBranchInput input,
        CancellationToken cancellationToken = default)
    {
        EnsureIcebergStream(stream, targetSink);
        var branchName = (input.BranchName ?? string.Empty).Trim();
        if (!TagNamePattern.IsMatch(branchName) || string.Equals(branchName, "main", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "待发布的 WAP 分支名必须由字母/数字开头（1-64 字符），且不可为保留分支 main。");
        }

        string? auditTagName = null;
        if (!string.IsNullOrWhiteSpace(input.CreateAuditTagName))
        {
            auditTagName = input.CreateAuditTagName.Trim();
            if (!TagNamePattern.IsMatch(auditTagName) || string.Equals(auditTagName, "main", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "可选审计标签名必须由字母/数字开头（1-64 字符），且不可为保留分支 main。");
            }
        }

        if (input.RequireContractPass && stream.HasDataContractConfigured && stream.ContractViolated)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData(
                    "Reason",
                    $"数据契约 SLA 当前处于违约状态（{stream.ContractViolationSummary ?? "未满足契约规则"}），WAP 发布门禁已阻断分支「{branchName}」发布到 main。");
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
                    .WithData("Reason", "目标 Apache Iceberg v2 表尚未产生任何快照，无法发布分支。");
            }

            var root = JsonNode.Parse(latestJson)?.AsObject()
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                    .WithData("Reason", "无法解析当前 Apache Iceberg v2 表元数据。");

            var refsObj = root["refs"] as JsonObject;
            var branchRef = refsObj?[branchName] as JsonObject;
            if (branchRef == null ||
                !string.Equals(branchRef["type"]?.GetValue<string>(), "branch", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"目标表中不存在命名分支「{branchName}」，请先在 WAP 分支模式写入变更后再发布。");
            }

            var branchSnapId = branchRef["snapshot-id"]?.GetValue<long>() ?? 0L;
            var snapshots = ExtractSnapshotsWithParent(root);
            var snapById = snapshots.ToDictionary(s => s.SnapshotId);
            if (branchSnapId <= 0 || !snapById.TryGetValue(branchSnapId, out var branchSnap))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"分支「{branchName}」指向的快照 {branchSnapId} 不存在于表元数据中。");
            }

            var prevMainSnapId = ResolveCurrentMainSnapshotId(root);
            long? prevMainVersion = prevMainSnapId.HasValue && snapById.TryGetValue(prevMainSnapId.Value, out var prevMainSnap)
                ? prevMainSnap.SequenceNumber
                : null;
            var nowUtc = DateTime.UtcNow;
            var currentMetaKey = config.BuildIcebergMetadataKey(destination, latestSeq);

            if (prevMainSnapId == branchSnapId)
            {
                return new CdcLakehouseBranchPromoteResultDto
                {
                    StreamId = stream.Id,
                    BranchName = branchName,
                    DryRun = input.DryRun,
                    AlreadyUpToDate = true,
                    PreviousMainSnapshotId = prevMainSnapId,
                    PreviousMainVersion = prevMainVersion,
                    PromotedSnapshotId = branchSnapId,
                    PromotedSnapshotVersion = branchSnap.SequenceNumber,
                    MetadataVersion = latestSeq,
                    MetadataKey = currentMetaKey,
                    AuditTagName = null,
                    PromotedAtUtc = nowUtc,
                    Message = $"分支 main 已指向分支「{branchName}」的最新快照 v{branchSnap.SequenceNumber}，无需重复发布。",
                };
            }

            if (prevMainSnapId is > 0 && !IsFastForwardAncestor(prevMainSnapId.Value, branchSnapId, snapById))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData(
                        "Reason",
                        $"分支 main（v{prevMainVersion?.ToString() ?? "?"}）不是分支「{branchName}」（v{branchSnap.SequenceNumber}）的直接祖先，已拒绝非快进覆盖以防止数据丢失。");
            }

            if (input.DryRun)
            {
                return new CdcLakehouseBranchPromoteResultDto
                {
                    StreamId = stream.Id,
                    BranchName = branchName,
                    DryRun = true,
                    AlreadyUpToDate = false,
                    PreviousMainSnapshotId = prevMainSnapId,
                    PreviousMainVersion = prevMainVersion,
                    PromotedSnapshotId = branchSnapId,
                    PromotedSnapshotVersion = branchSnap.SequenceNumber,
                    MetadataVersion = latestSeq,
                    MetadataKey = currentMetaKey,
                    AuditTagName = auditTagName,
                    PromotedAtUtc = nowUtc,
                    Message = $"Dry-Run 快进发布预检通过：main 将从 {(prevMainVersion.HasValue ? $"v{prevMainVersion.Value}" : "空分支")} 原子快进至「{branchName}」v{branchSnap.SequenceNumber}。",
                };
            }

            var nowMs = new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds();
            var nextMetaSeq = latestSeq + 1;
            var nextMetaKey = config.BuildIcebergMetadataKey(destination, nextMetaSeq);

            AppendMetadataLog(root, config.Bucket, currentMetaKey, nowMs);
            root["last-updated-ms"] = nowMs;
            root["current-snapshot-id"] = branchSnapId;

            refsObj ??= new JsonObject();
            refsObj["main"] = new JsonObject
            {
                ["snapshot-id"] = branchSnapId,
                ["type"] = "branch",
            };
            if (auditTagName != null)
            {
                refsObj[auditTagName] = new JsonObject
                {
                    ["snapshot-id"] = branchSnapId,
                    ["type"] = "tag",
                };
            }

            root["refs"] = refsObj;

            var snapLog = root["snapshot-log"] as JsonArray ?? new JsonArray();
            snapLog.Add(new JsonObject
            {
                ["timestamp-ms"] = nowMs,
                ["snapshot-id"] = branchSnapId,
            });
            root["snapshot-log"] = snapLog;

            var updatedJson = root.ToJsonString(IcebergV2CommitWriter.JsonOptions);
            if (await IcebergV2CommitWriter.PutMetadataIfAbsentAsync(
                    _httpClientFactory, _configuration, config, nextMetaKey, updatedJson, cancellationToken))
            {
                await IcebergV2CommitWriter.PutVersionHintAsync(
                    _httpClientFactory, _configuration, config, destination, nextMetaSeq, cancellationToken);
                PlatformMetrics.CdcIcebergMaintenanceTotal.WithLabels("promote", "succeeded").Inc();
                var tagNote = auditTagName != null ? $"，并原子绑定审计标签「{auditTagName}」" : string.Empty;
                return new CdcLakehouseBranchPromoteResultDto
                {
                    StreamId = stream.Id,
                    BranchName = branchName,
                    DryRun = false,
                    AlreadyUpToDate = false,
                    PreviousMainSnapshotId = prevMainSnapId,
                    PreviousMainVersion = prevMainVersion,
                    PromotedSnapshotId = branchSnapId,
                    PromotedSnapshotVersion = branchSnap.SequenceNumber,
                    MetadataVersion = nextMetaSeq,
                    MetadataKey = nextMetaKey,
                    AuditTagName = auditTagName,
                    PromotedAtUtc = nowUtc,
                    Message = $"已将分支「{branchName}」v{branchSnap.SequenceNumber} 原子快进发布到 main（元数据 v{nextMetaSeq}{tagNote}）。",
                };
            }
        }

        PlatformMetrics.CdcIcebergMaintenanceTotal.WithLabels("promote", "failed").Inc();
        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", "Apache Iceberg v2 分支快进发布元数据并发提交冲突，请重试。");
    }

    private static long? ResolveCurrentMainSnapshotId(JsonObject root)
    {
        if (root["refs"] is JsonObject refsObj &&
            refsObj["main"] is JsonObject mainRef &&
            mainRef["snapshot-id"] is JsonValue mVal &&
            mVal.TryGetValue<long>(out var mainSid) &&
            mainSid > 0)
        {
            return mainSid;
        }

        if (root["current-snapshot-id"] is JsonValue curVal &&
            curVal.TryGetValue<long>(out var curSid) &&
            curSid > 0)
        {
            return curSid;
        }

        return null;
    }

    private static bool IsFastForwardAncestor(
        long expectedAncestorId,
        long headSnapshotId,
        Dictionary<long, BranchSnapshotMeta> snapById)
    {
        var visited = new HashSet<long>();
        var cursor = (long?)headSnapshotId;
        while (cursor is > 0 && visited.Add(cursor.Value))
        {
            if (cursor.Value == expectedAncestorId)
            {
                return true;
            }

            if (!snapById.TryGetValue(cursor.Value, out var node))
            {
                return false;
            }

            cursor = node.ParentSnapshotId;
        }

        return false;
    }

    private static List<BranchSnapshotMeta> ExtractSnapshotsWithParent(JsonObject root)
    {
        var list = new List<BranchSnapshotMeta>();
        if (root["snapshots"] is not JsonArray snaps)
        {
            return list;
        }

        foreach (var node in snaps)
        {
            if (node is not JsonObject obj) continue;
            var seq = obj["sequence-number"]?.GetValue<long>() ?? 0L;
            var sid = obj["snapshot-id"]?.GetValue<long>() ?? 0L;
            long? parentId = obj["parent-snapshot-id"] is JsonValue pVal && pVal.TryGetValue<long>(out var p) && p > 0
                ? p
                : null;
            if (seq > 0 && sid > 0)
            {
                list.Add(new BranchSnapshotMeta(seq, sid, parentId));
            }
        }

        return list;
    }

    private sealed record BranchSnapshotMeta(long SequenceNumber, long SnapshotId, long? ParentSnapshotId);
}
