using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Monitoring;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// Apache Iceberg v2 原生 Schema Evolution 引擎（纯 .NET，零 JVM）：
/// 支持基于不可变 <c>FieldId</c> 的安全增列（<c>AddColumns</c>）、零数据重写列重命名（<c>RenameColumns</c>，保留原 FieldId 并同步扩展
/// <c>schema.name-mapping.default</c>）以及永不复用 FieldId 的安全删列（<c>DropColumns</c>，永久保护 1..12 核心信封列），
/// 并通过 S3 <c>If-None-Match: *</c> 原子推进 <c>schemas[]</c>、<c>current-schema-id</c> 与 <c>last-column-id</c>。
/// </summary>
public sealed partial class IcebergV2SchemaEvolutionService : ITransientDependency
{
    public const int MaxCoreEnvelopeFieldId = IcebergAvroManifestBuilder.BusinessKeyFieldId;
    private const int MaxOptimisticRetries = 16;

    private static readonly Regex ColumnNamePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "long", "double", "boolean", "timestamptz",
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new() { WriteIndented = false };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly OpenLineageEventEmitter? _openLineageEmitter;

    public IcebergV2SchemaEvolutionService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        OpenLineageEventEmitter? openLineageEmitter = null)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _openLineageEmitter = openLineageEmitter;
    }

    public async Task<CdcLakehouseSchemaEvolutionResultDto> EvolveSchemaAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        EvolveCdcLakehouseSchemaInput input,
        CancellationToken cancellationToken = default)
    {
        if (stream.TargetMode != CdcTargetMode.ExternalSink ||
            targetSink.Type != DataSourceType.ObjectStorage ||
            stream.LakehouseTableFormat != LakehouseTableFormat.ApacheIceberg ||
            string.IsNullOrWhiteSpace(stream.TargetSinkDestination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅目标为对象存储（S3/MinIO）且表格式为 Apache Iceberg v2 的 CDC 流支持原生 Schema Evolution。");
        }

        var addCols = input.AddColumns ?? new List<AddCdcLakehouseColumnInput>();
        var renameCols = input.RenameColumns ?? new List<RenameCdcLakehouseColumnInput>();
        var dropCols = input.DropColumns ?? new List<string>();
        if (addCols.Count == 0 && renameCols.Count == 0 && dropCols.Count == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "请至少指定一项列演进操作（新增、重命名或删除列）。");
        }

        ValidateInputs(addCols, renameCols, dropCols);

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
                    .WithData("Reason", "目标 Apache Iceberg v2 表尚未产生初始快照，请先写入至少一批数据后再执行 Schema Evolution。");
            }

            var root = JsonNode.Parse(latestJson)?.AsObject()
                ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                    .WithData("Reason", "无法解析当前 Apache Iceberg v2 表元数据。");

            var parsed = ParseActiveSchema(latestJson);
            var workingFields = parsed.Fields
                .Select(f => new MutableField(f.FieldId, f.Name, f.Type, f.Required, f.Doc))
                .ToList();
            var nameMapping = ParseNameMapping(root["properties"]?["schema.name-mapping.default"]?.GetValue<string>());
            EnsureAllFieldsInNameMapping(nameMapping, workingFields);

            var droppedCount = 0;
            foreach (var rawDrop in dropCols)
            {
                var colName = rawDrop.Trim();
                var target = workingFields.FirstOrDefault(f => string.Equals(f.Name, colName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"待删除的列「{colName}」不存在于当前活跃 Schema（schema-id={parsed.CurrentSchemaId}）中。");
                if (target.FieldId <= MaxCoreEnvelopeFieldId)
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"核心 CDC 信封列「{target.Name}」(FieldId={target.FieldId}) 受系统永久保护，不可删除。");
                }

                workingFields.Remove(target);
                droppedCount++;
            }

            var renamedCount = 0;
            foreach (var ren in renameCols)
            {
                var fromName = ren.FromName.Trim();
                var toName = ren.ToName.Trim();
                var target = workingFields.FirstOrDefault(f => string.Equals(f.Name, fromName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"待重命名的列「{fromName}」不存在于当前活跃 Schema 中。");
                if (target.FieldId <= MaxCoreEnvelopeFieldId)
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"核心 CDC 信封列「{target.Name}」(FieldId={target.FieldId}) 受系统永久保护，不可重命名。");
                }

                if (workingFields.Any(f => f != target && string.Equals(f.Name, toName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"目标列名「{toName}」已存在于当前活跃 Schema 中，禁止重名覆盖。");
                }

                target.Name = toName;
                AddAliasToNameMapping(nameMapping, target.FieldId, toName);
                renamedCount++;
            }

            var nextLastColumnId = Math.Max(parsed.LastColumnId, MaxCoreEnvelopeFieldId);
            var addedCount = 0;
            foreach (var add in addCols)
            {
                var colName = add.Name.Trim();
                var colType = add.Type.Trim().ToLowerInvariant();
                if (workingFields.Any(f => string.Equals(f.Name, colName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"新增列「{colName}」已存在于当前活跃 Schema 中。");
                }

                nextLastColumnId++;
                var docText = string.IsNullOrWhiteSpace(add.Doc) ? null : add.Doc.Trim();
                workingFields.Add(new MutableField(nextLastColumnId, colName, colType, required: false, doc: docText));
                AddAliasToNameMapping(nameMapping, nextLastColumnId, colName);
                addedCount++;
            }

            var schemasArr = root["schemas"] as JsonArray ?? new JsonArray();
            var maxSchemaId = parsed.CurrentSchemaId;
            foreach (var node in schemasArr)
            {
                if (node is JsonObject obj && obj["schema-id"] is JsonValue sv && sv.TryGetValue<int>(out var sid) && sid > maxSchemaId)
                {
                    maxSchemaId = sid;
                }
            }

            var newSchemaId = maxSchemaId + 1;
            var nowUtc = DateTime.UtcNow;
            var currentMetaKey = config.BuildIcebergMetadataKey(destination, latestSeq);
            var activeDtos = workingFields.Select(ToDto).ToList();

            if (input.DryRun)
            {
                return new CdcLakehouseSchemaEvolutionResultDto
                {
                    StreamId = stream.Id,
                    DryRun = true,
                    PreviousSchemaId = parsed.CurrentSchemaId,
                    NewSchemaId = newSchemaId,
                    LastColumnId = nextLastColumnId,
                    MetadataVersion = latestSeq,
                    MetadataKey = currentMetaKey,
                    AddedCount = addedCount,
                    RenamedCount = renamedCount,
                    DroppedCount = droppedCount,
                    ActiveFields = activeDtos,
                    EvolvedAtUtc = nowUtc,
                    Message = $"Dry-Run Schema Evolution 预演通过：schema-id 将由 {parsed.CurrentSchemaId} 演进至 {newSchemaId}（+{addedCount} 新增 / ~{renamedCount} 重命名 / -{droppedCount} 移除，last-column-id={nextLastColumnId}）。",
                };
            }

            var nowMs = new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds();
            var nextMetaSeq = latestSeq + 1;
            var nextMetaKey = config.BuildIcebergMetadataKey(destination, nextMetaSeq);

            var metaLog = root["metadata-log"] as JsonArray ?? new JsonArray();
            var prevUpdated = root["last-updated-ms"]?.GetValue<long>() ?? nowMs;
            metaLog.Add(new JsonObject
            {
                ["timestamp-ms"] = prevUpdated,
                ["metadata-file"] = $"s3://{config.Bucket}/{currentMetaKey}",
            });
            root["metadata-log"] = metaLog;
            root["last-updated-ms"] = nowMs;
            root["last-column-id"] = nextLastColumnId;
            root["current-schema-id"] = newSchemaId;

            var newFieldsArr = new JsonArray();
            foreach (var f in workingFields)
            {
                var fieldObj = new JsonObject
                {
                    ["id"] = f.FieldId,
                    ["name"] = f.Name,
                    ["required"] = f.Required,
                    ["type"] = f.Type,
                };
                if (!string.IsNullOrWhiteSpace(f.Doc))
                {
                    fieldObj["doc"] = f.Doc;
                }

                newFieldsArr.Add(fieldObj);
            }

            schemasArr.Add(new JsonObject
            {
                ["type"] = "struct",
                ["schema-id"] = newSchemaId,
                ["fields"] = newFieldsArr,
            });
            root["schemas"] = schemasArr;

            var propsObj = root["properties"] as JsonObject ?? new JsonObject();
            propsObj["schema.name-mapping.default"] = SerializeNameMapping(nameMapping);
            root["properties"] = propsObj;

            var updatedJson = root.ToJsonString(IcebergV2CommitWriter.JsonOptions);
            if (await IcebergV2CommitWriter.PutMetadataIfAbsentAsync(
                    _httpClientFactory, _configuration, config, nextMetaKey, updatedJson, cancellationToken))
            {
                await IcebergV2CommitWriter.PutVersionHintAsync(
                    _httpClientFactory, _configuration, config, destination, nextMetaSeq, cancellationToken);
                PlatformMetrics.CdcIcebergMaintenanceTotal.WithLabels("schema_evolve", "succeeded").Inc();
                if (_openLineageEmitter != null)
                {
                    var curSnap = root["current-snapshot-id"]?.GetValue<long>() ?? 0L;
                    await _openLineageEmitter.TryEmitLakehouseLifecycleEventAsync(
                        "schema_evolve", stream, destination, nextMetaSeq, curSnap, nextMetaSeq, $"schema-v{newSchemaId}", cancellationToken);
                }

                return new CdcLakehouseSchemaEvolutionResultDto
                {
                    StreamId = stream.Id,
                    DryRun = false,
                    PreviousSchemaId = parsed.CurrentSchemaId,
                    NewSchemaId = newSchemaId,
                    LastColumnId = nextLastColumnId,
                    MetadataVersion = nextMetaSeq,
                    MetadataKey = nextMetaKey,
                    AddedCount = addedCount,
                    RenamedCount = renamedCount,
                    DroppedCount = droppedCount,
                    ActiveFields = activeDtos,
                    EvolvedAtUtc = nowUtc,
                    Message = $"Apache Iceberg v2 表结构已原子演进至 schema-id={newSchemaId}（元数据 v{nextMetaSeq}：+{addedCount} 新增 / ~{renamedCount} 重命名 / -{droppedCount} 移除，last-column-id={nextLastColumnId}）。",
                };
            }
        }

        PlatformMetrics.CdcIcebergMaintenanceTotal.WithLabels("schema_evolve", "failed").Inc();
        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", "Apache Iceberg v2 Schema Evolution 元数据并发提交冲突，请重试。");
    }
}
