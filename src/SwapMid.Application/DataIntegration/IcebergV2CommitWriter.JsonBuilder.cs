using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwapMid.DataIntegration;

internal static partial class IcebergV2CommitWriter
{
    internal static bool TryExtractSnapshotManifestListUri(string metadataJson, long snapshotId, out string? manifestListUri)
    {
        manifestListUri = null;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("snapshots", out var snaps) || snaps.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var snap in snaps.EnumerateArray())
            {
                if (snap.TryGetProperty("snapshot-id", out var sid) &&
                    sid.TryGetInt64(out var val) &&
                    val == snapshotId &&
                    snap.TryGetProperty("manifest-list", out var ml) &&
                    ml.ValueKind == JsonValueKind.String)
                {
                    manifestListUri = ml.GetString();
                    return !string.IsNullOrWhiteSpace(manifestListUri);
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
    internal static string BuildTableMetadataJson(
        CdcStreamDefinition stream,
        string tableLocation,
        string bucket,
        string? previousMetadataJson,
        string? previousMetadataKey,
        long nextSeq,
        long snapshotId,
        string manifestKey,
        string operation,
        string snapshotMode,
        string runToken,
        long timestampMs,
        IReadOnlyList<IcebergManifestEntry> addedFiles,
        IReadOnlyList<string> removedRelativePaths,
        IReadOnlyList<IcebergManifestEntry> activeFiles)
    {
        var snapshots = new JsonArray();
        var snapshotLog = new JsonArray();
        var metadataLog = new JsonArray();
        long? parentSnapshotId = null;

        if (!string.IsNullOrWhiteSpace(previousMetadataJson))
        {
            try
            {
                var prevNode = JsonNode.Parse(previousMetadataJson)?.AsObject();
                if (prevNode != null)
                {
                    if (prevNode["current-snapshot-id"] is JsonValue csVal && csVal.TryGetValue<long>(out var pSnap))
                    {
                        parentSnapshotId = pSnap;
                    }

                    if (prevNode["snapshots"] is JsonArray prevSnaps)
                    {
                        foreach (var item in prevSnaps)
                        {
                            if (item != null) snapshots.Add(item.DeepClone());
                        }
                    }

                    if (prevNode["snapshot-log"] is JsonArray prevLog)
                    {
                        foreach (var item in prevLog)
                        {
                            if (item != null) snapshotLog.Add(item.DeepClone());
                        }
                    }

                    if (prevNode["metadata-log"] is JsonArray prevMetaLog)
                    {
                        foreach (var item in prevMetaLog)
                        {
                            if (item != null) metadataLog.Add(item.DeepClone());
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(previousMetadataKey))
                    {
                        var prevUpdated = prevNode["last-updated-ms"]?.GetValue<long>() ?? timestampMs;
                        metadataLog.Add(new JsonObject
                        {
                            ["timestamp-ms"] = prevUpdated,
                            ["metadata-file"] = $"s3://{bucket}/{previousMetadataKey}",
                        });
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore corrupted previous metadata when building next sequence.
            }
        }

        var addedRecords = addedFiles.Sum(f => f.RecordCount);
        var totalRecords = activeFiles.Sum(f => f.RecordCount);
        var addedBytes = addedFiles.Sum(f => f.FileSizeInBytes);
        var totalBytes = activeFiles.Sum(f => f.FileSizeInBytes);

        var summaryObj = new JsonObject
        {
            ["operation"] = operation,
            ["added-data-files"] = addedFiles.Count.ToString(CultureInfo.InvariantCulture),
            ["deleted-data-files"] = removedRelativePaths.Count.ToString(CultureInfo.InvariantCulture),
            ["added-records"] = addedRecords.ToString(CultureInfo.InvariantCulture),
            ["added-files-size"] = addedBytes.ToString(CultureInfo.InvariantCulture),
            ["total-data-files"] = activeFiles.Count.ToString(CultureInfo.InvariantCulture),
            ["total-records"] = totalRecords.ToString(CultureInfo.InvariantCulture),
            ["total-files-size"] = totalBytes.ToString(CultureInfo.InvariantCulture),
            ["swapmid-run-id"] = runToken,
            ["swapmid-snapshot-mode"] = snapshotMode,
            ["engine-name"] = EngineName,
        };

        var snapObj = new JsonObject
        {
            ["sequence-number"] = nextSeq,
            ["snapshot-id"] = snapshotId,
            ["timestamp-ms"] = timestampMs,
            ["manifest-list"] = $"s3://{bucket}/{manifestKey}",
            ["schema-id"] = 0,
            ["summary"] = summaryObj,
        };
        if (parentSnapshotId.HasValue)
        {
            snapObj["parent-snapshot-id"] = parentSnapshotId.Value;
        }

        snapshots.Add(snapObj);
        snapshotLog.Add(new JsonObject
        {
            ["timestamp-ms"] = timestampMs,
            ["snapshot-id"] = snapshotId,
        });

        var isDailyPartitioned = stream.LakehousePartitionStrategy == LakehousePartitionStrategy.DailyCommitUtc;
        var partitionSpecs = new JsonArray
        {
            new JsonObject { ["spec-id"] = 0, ["fields"] = new JsonArray() },
        };
        if (isDailyPartitioned)
        {
            partitionSpecs.Add(new JsonObject
            {
                ["spec-id"] = IcebergAvroManifestBuilder.DailyPartitionSpecId,
                ["fields"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "commit_timestamp_utc_day",
                        ["transform"] = "day",
                        ["source-id"] = 8,
                        ["field-id"] = IcebergAvroManifestBuilder.DailyPartitionFieldId,
                    },
                },
            });
        }

        var root = new JsonObject
        {
            ["format-version"] = 2,
            ["table-uuid"] = stream.Id.ToString("D"),
            ["location"] = tableLocation,
            ["last-sequence-number"] = nextSeq,
            ["last-updated-ms"] = timestampMs,
            ["last-column-id"] = 11,
            ["current-schema-id"] = 0,
            ["schemas"] = new JsonArray { BuildCdcIcebergSchemaNode() },
            ["default-spec-id"] = isDailyPartitioned ? IcebergAvroManifestBuilder.DailyPartitionSpecId : 0,
            ["partition-specs"] = partitionSpecs,
            ["last-partition-id"] = isDailyPartitioned ? IcebergAvroManifestBuilder.DailyPartitionFieldId : 999,
            ["default-sort-order-id"] = 0,
            ["sort-orders"] = new JsonArray
            {
                new JsonObject { ["order-id"] = 0, ["fields"] = new JsonArray() },
            },
            ["properties"] = new JsonObject
            {
                ["write.format.default"] = "parquet",
                ["schema.name-mapping.default"] = IcebergAvroManifestBuilder.CdcDefaultNameMappingJson,
                ["swapmid.stream_id"] = stream.Id.ToString("D"),
                ["swapmid.stream_name"] = stream.Name,
                ["swapmid.engine"] = EngineName,
                ["swapmid.partition_strategy"] = isDailyPartitioned ? "day(commit_timestamp_utc)" : "none",
            },
            ["current-snapshot-id"] = snapshotId,
            ["refs"] = new JsonObject
            {
                ["main"] = new JsonObject
                {
                    ["snapshot-id"] = snapshotId,
                    ["type"] = "branch",
                },
            },
            ["snapshots"] = snapshots,
            ["snapshot-log"] = snapshotLog,
            ["metadata-log"] = metadataLog,
        };

        return root.ToJsonString(JsonOptions);
    }

    private static JsonObject BuildCdcIcebergSchemaNode()
    {
        var cols = new (string Name, string Type)[]
        {
            ("event_id", "string"),
            ("stream_id", "string"),
            ("stream_name", "string"),
            ("source_schema", "string"),
            ("source_table", "string"),
            ("op", "string"),
            ("lsn", "string"),
            ("commit_timestamp_utc", "timestamptz"),
            ("changed_columns", "string"),
            ("after_json", "string"),
            ("before_json", "string"),
        };
        var fields = new JsonArray();
        for (var i = 0; i < cols.Length; i++)
        {
            fields.Add(new JsonObject
            {
                ["id"] = i + 1,
                ["name"] = cols[i].Name,
                ["required"] = false,
                ["type"] = cols[i].Type,
            });
        }

        return new JsonObject
        {
            ["type"] = "struct",
            ["schema-id"] = 0,
            ["fields"] = fields,
        };
    }

    private static bool TryExtractCurrentSnapshotId(string metadataJson, out long snapshotId)
    {
        snapshotId = 0;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            return doc.RootElement.TryGetProperty("current-snapshot-id", out var prop) &&
                   prop.TryGetInt64(out snapshotId) &&
                   snapshotId > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindExistingReceiptForRunId(
        string metadataJson,
        ObjectStorageSinkConfig config,
        string destination,
        string runToken,
        out IcebergCommitReceipt receipt)
    {
        receipt = default;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("snapshots", out var snaps) || snaps.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var snap in snaps.EnumerateArray())
            {
                if (!snap.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (summary.TryGetProperty("swapmid-run-id", out var runProp) &&
                    string.Equals(runProp.GetString(), runToken, StringComparison.OrdinalIgnoreCase) &&
                    snap.TryGetProperty("sequence-number", out var seqProp) &&
                    seqProp.TryGetInt64(out var seq) &&
                    snap.TryGetProperty("snapshot-id", out var sidProp) &&
                    sidProp.TryGetInt64(out var sid))
                {
                    var mlKey = snap.TryGetProperty("manifest-list", out var mlProp) && mlProp.ValueKind == JsonValueKind.String
                        ? IcebergAvroManifestBuilder.StripS3BucketPrefix(mlProp.GetString()!, config.Bucket)
                        : config.BuildIcebergManifestKey(destination, sid);
                    receipt = new IcebergCommitReceipt(
                        seq,
                        sid,
                        config.BuildIcebergMetadataKey(destination, seq),
                        mlKey);
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
