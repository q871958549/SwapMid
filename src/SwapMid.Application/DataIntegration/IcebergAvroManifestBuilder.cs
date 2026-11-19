using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwapMid.DataIntegration;

/// <summary>
/// Apache Iceberg v2 标准 Avro <c>manifest-file</c>（<c>manifest_entry</c>）与
/// <c>manifest-list</c>（<c>manifest_file</c>）编码与解码器，支持无分区（spec-id=0）与
/// 按日隐藏分区（<c>day(commit_timestamp_utc)</c>，spec-id=1，field-id=1000）及 Parquet <c>schema.name-mapping.default</c>。
/// </summary>
internal static partial class IcebergAvroManifestBuilder
{
    internal const int StatusExisting = 0;
    internal const int StatusAdded = 1;
    internal const int StatusDeleted = 2;

    internal const int ContentData = 0;
    internal const int ContentEqualityDeletes = 2;
    internal const int ManifestContentData = 0;
    internal const int ManifestContentDeletes = 1;
    internal const int BusinessKeyFieldId = 12;

    internal const int UnpartitionedSpecId = 0;
    internal const int DailyPartitionSpecId = 1;
    internal const int DailyPartitionFieldId = 1000;

    internal const string DailyPartitionSpecJson =
        """[{"name":"commit_timestamp_utc_day","transform":"day","source-id":8,"field-id":1000}]""";

    internal sealed record ManifestEntryRecord(
        int Status,
        long SnapshotId,
        long SequenceNumber,
        long FileSequenceNumber,
        string FilePath,
        string FileFormat,
        long RecordCount,
        long FileSizeInBytes,
        int? PartitionDayEpoch = null,
        int Content = ContentData,
        int[]? EqualityIds = null);

    internal sealed record ManifestListEntryRecord(
        string ManifestPath,
        long ManifestLength,
        int PartitionSpecId,
        int Content,
        long SequenceNumber,
        long MinSequenceNumber,
        long AddedSnapshotId,
        int AddedFilesCount,
        int ExistingFilesCount,
        int DeletedFilesCount,
        long AddedRowsCount,
        long ExistingRowsCount,
        long DeletedRowsCount,
        int? MinPartitionDayEpoch = null,
        int? MaxPartitionDayEpoch = null,
        bool PartitionContainsNull = false);

    internal const string ManifestListAvroSchemaJson = """
        {"type":"record","name":"manifest_file","fields":[{"name":"manifest_path","type":"string","field-id":500},{"name":"manifest_length","type":"long","field-id":501},{"name":"partition_spec_id","type":"int","field-id":502},{"name":"content","type":"int","field-id":517},{"name":"sequence_number","type":"long","field-id":515},{"name":"min_sequence_number","type":"long","field-id":516},{"name":"added_snapshot_id","type":"long","field-id":503},{"name":"added_files_count","type":"int","field-id":504},{"name":"existing_files_count","type":"int","field-id":505},{"name":"deleted_files_count","type":"int","field-id":506},{"name":"added_rows_count","type":"long","field-id":512},{"name":"existing_rows_count","type":"long","field-id":513},{"name":"deleted_rows_count","type":"long","field-id":514},{"name":"partitions","type":["null",{"type":"array","items":{"type":"record","name":"r508","fields":[{"name":"contains_null","type":"boolean","field-id":509},{"name":"contains_nan","type":["null","boolean"],"default":null,"field-id":518},{"name":"lower_bound","type":["null","bytes"],"default":null,"field-id":510},{"name":"upper_bound","type":["null","bytes"],"default":null,"field-id":511}]},"element-id":508}],"default":null,"field-id":507}]}
        """;

    internal const string ManifestFileAvroSchemaJson = """
        {"type":"record","name":"manifest_entry","fields":[{"name":"status","type":"int","field-id":0},{"name":"snapshot_id","type":["null","long"],"default":null,"field-id":1},{"name":"sequence_number","type":["null","long"],"default":null,"field-id":3},{"name":"file_sequence_number","type":["null","long"],"default":null,"field-id":4},{"name":"data_file","type":{"type":"record","name":"r2","fields":[{"name":"content","type":"int","field-id":134},{"name":"file_path","type":"string","field-id":100},{"name":"file_format","type":"string","field-id":101},{"name":"partition","type":{"type":"record","name":"r102","fields":[]},"field-id":102},{"name":"record_count","type":"long","field-id":103},{"name":"file_size_in_bytes","type":"long","field-id":104},{"name":"column_sizes","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k117_v118","fields":[{"name":"key","type":"int","field-id":117},{"name":"value","type":"long","field-id":118}]}}],"default":null,"field-id":108},{"name":"value_counts","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k119_v120","fields":[{"name":"key","type":"int","field-id":119},{"name":"value","type":"long","field-id":120}]}}],"default":null,"field-id":109},{"name":"null_value_counts","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k121_v122","fields":[{"name":"key","type":"int","field-id":121},{"name":"value","type":"long","field-id":122}]}}],"default":null,"field-id":110},{"name":"nan_value_counts","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k138_v139","fields":[{"name":"key","type":"int","field-id":138},{"name":"value","type":"long","field-id":139}]}}],"default":null,"field-id":137},{"name":"lower_bounds","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k126_v127","fields":[{"name":"key","type":"int","field-id":126},{"name":"value","type":"bytes","field-id":127}]}}],"default":null,"field-id":125},{"name":"upper_bounds","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k129_v130","fields":[{"name":"key","type":"int","field-id":129},{"name":"value","type":"bytes","field-id":130}]}}],"default":null,"field-id":128},{"name":"key_metadata","type":["null","bytes"],"default":null,"field-id":131},{"name":"split_offsets","type":["null",{"type":"array","items":"long","element-id":133}],"default":null,"field-id":132},{"name":"equality_ids","type":["null",{"type":"array","items":"int","element-id":136}],"default":null,"field-id":135},{"name":"sort_order_id","type":["null","int"],"default":null,"field-id":140}]},"field-id":2}]}
        """;

    internal const string ManifestFileDailyPartitionedAvroSchemaJson = """
        {"type":"record","name":"manifest_entry","fields":[{"name":"status","type":"int","field-id":0},{"name":"snapshot_id","type":["null","long"],"default":null,"field-id":1},{"name":"sequence_number","type":["null","long"],"default":null,"field-id":3},{"name":"file_sequence_number","type":["null","long"],"default":null,"field-id":4},{"name":"data_file","type":{"type":"record","name":"r2","fields":[{"name":"content","type":"int","field-id":134},{"name":"file_path","type":"string","field-id":100},{"name":"file_format","type":"string","field-id":101},{"name":"partition","type":{"type":"record","name":"r102","fields":[{"name":"commit_timestamp_utc_day","type":["null","int"],"default":null,"field-id":1000}]},"field-id":102},{"name":"record_count","type":"long","field-id":103},{"name":"file_size_in_bytes","type":"long","field-id":104},{"name":"column_sizes","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k117_v118","fields":[{"name":"key","type":"int","field-id":117},{"name":"value","type":"long","field-id":118}]}}],"default":null,"field-id":108},{"name":"value_counts","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k119_v120","fields":[{"name":"key","type":"int","field-id":119},{"name":"value","type":"long","field-id":120}]}}],"default":null,"field-id":109},{"name":"null_value_counts","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k121_v122","fields":[{"name":"key","type":"int","field-id":121},{"name":"value","type":"long","field-id":122}]}}],"default":null,"field-id":110},{"name":"nan_value_counts","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k138_v139","fields":[{"name":"key","type":"int","field-id":138},{"name":"value","type":"long","field-id":139}]}}],"default":null,"field-id":137},{"name":"lower_bounds","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k126_v127","fields":[{"name":"key","type":"int","field-id":126},{"name":"value","type":"bytes","field-id":127}]}}],"default":null,"field-id":125},{"name":"upper_bounds","type":["null",{"type":"array","logicalType":"map","items":{"type":"record","name":"k129_v130","fields":[{"name":"key","type":"int","field-id":129},{"name":"value","type":"bytes","field-id":130}]}}],"default":null,"field-id":128},{"name":"key_metadata","type":["null","bytes"],"default":null,"field-id":131},{"name":"split_offsets","type":["null",{"type":"array","items":"long","element-id":133}],"default":null,"field-id":132},{"name":"equality_ids","type":["null",{"type":"array","items":"int","element-id":136}],"default":null,"field-id":135},{"name":"sort_order_id","type":["null","int"],"default":null,"field-id":140}]},"field-id":2}]}
        """;

    internal static readonly string CdcDefaultNameMappingJson = BuildCdcNameMappingJson();

    internal static int ToUtcEpochDay(DateTime timestampUtc)
        => (int)(DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc).Date - DateTime.UnixEpoch).TotalDays;

    internal static string FormatUtcDayDate(int epochDay)
        => DateTime.UnixEpoch.AddDays(epochDay).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string FormatUtcDayPartitionSegment(int epochDay)
        => $"data/commit_day={FormatUtcDayDate(epochDay)}";

    internal static int? TryExtractUtcDayFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var match = CommitDayRegex().Match(path);
        if (!match.Success) return null;
        if (DateTime.TryParseExact(
                match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            return ToUtcEpochDay(dt.ToUniversalTime());
        }

        return null;
    }

    internal static byte[] BuildManifestFileAvro(
        string icebergSchemaJson,
        IReadOnlyList<ManifestEntryRecord> entries,
        string syncSeed,
        int partitionSpecId = UnpartitionedSpecId,
        int manifestContent = ManifestContentData)
    {
        var isDaily = partitionSpecId == DailyPartitionSpecId;
        using var block = new MemoryStream(entries.Count * 176 + 64);
        foreach (var entry in entries)
        {
            IcebergAvroOcfCodec.WriteInt(block, entry.Status);
            IcebergAvroOcfCodec.WriteNullableLong(block, entry.SnapshotId);
            IcebergAvroOcfCodec.WriteNullableLong(block, entry.SequenceNumber);
            IcebergAvroOcfCodec.WriteNullableLong(block, entry.FileSequenceNumber);

            // data_file (record r2)
            IcebergAvroOcfCodec.WriteInt(block, entry.Content);
            IcebergAvroOcfCodec.WriteString(block, entry.FilePath);
            IcebergAvroOcfCodec.WriteString(block, entry.FileFormat);
            if (isDaily)
            {
                IcebergAvroOcfCodec.WriteNullableInt(block, entry.PartitionDayEpoch);
            }

            IcebergAvroOcfCodec.WriteLong(block, entry.RecordCount);
            IcebergAvroOcfCodec.WriteLong(block, entry.FileSizeInBytes);
            for (var i = 0; i < 8; i++)
            {
                IcebergAvroOcfCodec.WriteLong(block, 0);
            }

            if (entry.EqualityIds is { Length: > 0 })
            {
                IcebergAvroOcfCodec.WriteLong(block, 1); // union branch 1 (array<int>)
                IcebergAvroOcfCodec.WriteLong(block, entry.EqualityIds.Length);
                foreach (var eqId in entry.EqualityIds)
                {
                    IcebergAvroOcfCodec.WriteInt(block, eqId);
                }

                IcebergAvroOcfCodec.WriteLong(block, 0); // end of array
            }
            else
            {
                IcebergAvroOcfCodec.WriteLong(block, 0); // equality_ids = null
            }

            IcebergAvroOcfCodec.WriteNullableInt(block, 0); // sort_order_id = 0
        }

        var meta = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["avro.schema"] = isDaily ? ManifestFileDailyPartitionedAvroSchemaJson : ManifestFileAvroSchemaJson,
            ["avro.codec"] = "null",
            ["schema"] = icebergSchemaJson,
            ["schema-id"] = "0",
            ["partition-spec"] = isDaily ? DailyPartitionSpecJson : "[]",
            ["partition-spec-id"] = isDaily ? "1" : "0",
            ["format-version"] = "2",
            ["content"] = manifestContent == ManifestContentDeletes ? "deletes" : "data",
        };
        return IcebergAvroOcfCodec.WriteSingleBlockFile(meta, entries.Count, block.ToArray(), syncSeed);
    }

    internal static byte[] BuildManifestListAvro(
        long snapshotId,
        long? parentSnapshotId,
        long sequenceNumber,
        IReadOnlyList<ManifestListEntryRecord> manifests,
        string syncSeed)
    {
        using var block = new MemoryStream(manifests.Count * 192 + 64);
        foreach (var item in manifests)
        {
            IcebergAvroOcfCodec.WriteString(block, item.ManifestPath);
            IcebergAvroOcfCodec.WriteLong(block, item.ManifestLength);
            IcebergAvroOcfCodec.WriteInt(block, item.PartitionSpecId);
            IcebergAvroOcfCodec.WriteInt(block, item.Content);
            IcebergAvroOcfCodec.WriteLong(block, item.SequenceNumber);
            IcebergAvroOcfCodec.WriteLong(block, item.MinSequenceNumber);
            IcebergAvroOcfCodec.WriteLong(block, item.AddedSnapshotId);
            IcebergAvroOcfCodec.WriteInt(block, item.AddedFilesCount);
            IcebergAvroOcfCodec.WriteInt(block, item.ExistingFilesCount);
            IcebergAvroOcfCodec.WriteInt(block, item.DeletedFilesCount);
            IcebergAvroOcfCodec.WriteLong(block, item.AddedRowsCount);
            IcebergAvroOcfCodec.WriteLong(block, item.ExistingRowsCount);
            IcebergAvroOcfCodec.WriteLong(block, item.DeletedRowsCount);

            if (item.PartitionSpecId == DailyPartitionSpecId)
            {
                IcebergAvroOcfCodec.WriteLong(block, 1); // partitions union branch 1 (array<r508>)
                IcebergAvroOcfCodec.WriteLong(block, 1); // 1 field summary
                IcebergAvroOcfCodec.WriteBool(block, item.PartitionContainsNull);
                IcebergAvroOcfCodec.WriteLong(block, 1); // contains_nan branch 1
                IcebergAvroOcfCodec.WriteBool(block, false);
                if (item.MinPartitionDayEpoch.HasValue && item.MaxPartitionDayEpoch.HasValue)
                {
                    var minBytes = new byte[4];
                    var maxBytes = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(minBytes, item.MinPartitionDayEpoch.Value);
                    BinaryPrimitives.WriteInt32LittleEndian(maxBytes, item.MaxPartitionDayEpoch.Value);
                    IcebergAvroOcfCodec.WriteLong(block, 1);
                    IcebergAvroOcfCodec.WriteBytes(block, minBytes);
                    IcebergAvroOcfCodec.WriteLong(block, 1);
                    IcebergAvroOcfCodec.WriteBytes(block, maxBytes);
                }
                else
                {
                    IcebergAvroOcfCodec.WriteLong(block, 0);
                    IcebergAvroOcfCodec.WriteLong(block, 0);
                }

                IcebergAvroOcfCodec.WriteLong(block, 0); // end of array
            }
            else
            {
                IcebergAvroOcfCodec.WriteLong(block, 0); // partitions = null
            }
        }

        var meta = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["avro.schema"] = ManifestListAvroSchemaJson,
            ["avro.codec"] = "null",
            ["format-version"] = "2",
            ["snapshot-id"] = snapshotId.ToString(CultureInfo.InvariantCulture),
            ["sequence-number"] = sequenceNumber.ToString(CultureInfo.InvariantCulture),
        };
        if (parentSnapshotId.HasValue)
        {
            meta["parent-snapshot-id"] = parentSnapshotId.Value.ToString(CultureInfo.InvariantCulture);
        }

        return IcebergAvroOcfCodec.WriteSingleBlockFile(meta, manifests.Count, block.ToArray(), syncSeed);
    }

    internal static List<ManifestListEntryRecord> ReadManifestListAvro(byte[] avroBytes)
    {
        var ocf = IcebergAvroOcfCodec.ReadFile(avroBytes);
        var entries = new List<ManifestListEntryRecord>();
        foreach (var block in ocf.Blocks)
        {
            using var ms = new MemoryStream(block, writable: false);
            while (ms.Position < ms.Length)
            {
                var path = IcebergAvroOcfCodec.ReadString(ms);
                var len = IcebergAvroOcfCodec.ReadLong(ms);
                var specId = IcebergAvroOcfCodec.ReadInt(ms);
                var content = IcebergAvroOcfCodec.ReadInt(ms);
                var seq = IcebergAvroOcfCodec.ReadLong(ms);
                var minSeq = IcebergAvroOcfCodec.ReadLong(ms);
                var addedSnap = IcebergAvroOcfCodec.ReadLong(ms);
                var addedFiles = IcebergAvroOcfCodec.ReadInt(ms);
                var existingFiles = IcebergAvroOcfCodec.ReadInt(ms);
                var deletedFiles = IcebergAvroOcfCodec.ReadInt(ms);
                var addedRows = IcebergAvroOcfCodec.ReadLong(ms);
                var existingRows = IcebergAvroOcfCodec.ReadLong(ms);
                var deletedRows = IcebergAvroOcfCodec.ReadLong(ms);

                int? minDay = null;
                int? maxDay = null;
                var containsNull = false;
                if (IcebergAvroOcfCodec.ReadLong(ms) != 0)
                {
                    (minDay, maxDay, containsNull) = ReadFirstPartitionBoundsAndSkipRest(ms);
                }

                entries.Add(new ManifestListEntryRecord(
                    path, len, specId, content, seq, minSeq, addedSnap,
                    addedFiles, existingFiles, deletedFiles, addedRows, existingRows, deletedRows,
                    minDay, maxDay, containsNull));
            }
        }

        return entries;
    }

    internal static List<ManifestEntryRecord> ReadManifestFileAvro(byte[] avroBytes)
    {
        var ocf = IcebergAvroOcfCodec.ReadFile(avroBytes);
        var isDaily = ocf.Metadata.TryGetValue("partition-spec-id", out var sid) && sid == "1";
        var hasEqualityIds = ocf.Metadata.TryGetValue("avro.schema", out var rawSchema) &&
                             rawSchema.Contains("\"equality_ids\"", StringComparison.Ordinal);
        var entries = new List<ManifestEntryRecord>();
        foreach (var block in ocf.Blocks)
        {
            using var ms = new MemoryStream(block, writable: false);
            while (ms.Position < ms.Length)
            {
                var status = IcebergAvroOcfCodec.ReadInt(ms);
                var snapId = IcebergAvroOcfCodec.ReadNullableLong(ms) ?? 0L;
                var seq = IcebergAvroOcfCodec.ReadNullableLong(ms) ?? 0L;
                var fileSeq = IcebergAvroOcfCodec.ReadNullableLong(ms) ?? seq;

                var content = IcebergAvroOcfCodec.ReadInt(ms);
                var filePath = IcebergAvroOcfCodec.ReadString(ms);
                var fileFormat = IcebergAvroOcfCodec.ReadString(ms);
                int? partitionDay = isDaily ? IcebergAvroOcfCodec.ReadNullableInt(ms) : null;
                var recordCount = IcebergAvroOcfCodec.ReadLong(ms);
                var fileSize = IcebergAvroOcfCodec.ReadLong(ms);

                for (var m = 0; m < 4; m++) SkipNullableMapArray(ms, valueIsBytes: false);
                for (var m = 0; m < 2; m++) SkipNullableMapArray(ms, valueIsBytes: true);
                if (IcebergAvroOcfCodec.ReadLong(ms) != 0) _ = IcebergAvroOcfCodec.ReadBytes(ms);
                if (IcebergAvroOcfCodec.ReadLong(ms) != 0) SkipLongArray(ms);
                int[]? equalityIds = null;
                if (hasEqualityIds && IcebergAvroOcfCodec.ReadLong(ms) != 0)
                {
                    equalityIds = ReadIntArray(ms);
                }

                _ = IcebergAvroOcfCodec.ReadNullableInt(ms);

                entries.Add(new ManifestEntryRecord(
                    status, snapId, seq, fileSeq, filePath, fileFormat, recordCount, fileSize, partitionDay, content, equalityIds));
            }
        }

        return entries;
    }

    internal static string StripS3BucketPrefix(string s3Uri, string bucket)
    {
        var prefix = $"s3://{bucket}/";
        return s3Uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? s3Uri[prefix.Length..]
            : s3Uri.TrimStart('/');
    }

    private static (int? MinDay, int? MaxDay, bool ContainsNull) ReadFirstPartitionBoundsAndSkipRest(Stream ms)
    {
        int? minDay = null;
        int? maxDay = null;
        var containsNull = false;
        var captured = false;

        while (true)
        {
            var count = IcebergAvroOcfCodec.ReadLong(ms);
            if (count == 0) break;
            if (count < 0) { count = -count; _ = IcebergAvroOcfCodec.ReadLong(ms); }
            for (long i = 0; i < count; i++)
            {
                var cNull = IcebergAvroOcfCodec.ReadBool(ms);
                if (IcebergAvroOcfCodec.ReadLong(ms) != 0) _ = IcebergAvroOcfCodec.ReadBool(ms);
                var lo = IcebergAvroOcfCodec.ReadLong(ms) != 0 ? IcebergAvroOcfCodec.ReadBytes(ms) : null;
                var hi = IcebergAvroOcfCodec.ReadLong(ms) != 0 ? IcebergAvroOcfCodec.ReadBytes(ms) : null;
                if (!captured)
                {
                    captured = true;
                    containsNull = cNull;
                    if (lo is { Length: 4 }) minDay = BinaryPrimitives.ReadInt32LittleEndian(lo);
                    if (hi is { Length: 4 }) maxDay = BinaryPrimitives.ReadInt32LittleEndian(hi);
                }
            }
        }

        return (minDay, maxDay, containsNull);
    }

    private static void SkipNullableMapArray(Stream ms, bool valueIsBytes)
    {
        if (IcebergAvroOcfCodec.ReadLong(ms) == 0) return;
        while (true)
        {
            var count = IcebergAvroOcfCodec.ReadLong(ms);
            if (count == 0) break;
            if (count < 0) { count = -count; _ = IcebergAvroOcfCodec.ReadLong(ms); }
            for (long i = 0; i < count; i++)
            {
                _ = IcebergAvroOcfCodec.ReadInt(ms);
                if (valueIsBytes) _ = IcebergAvroOcfCodec.ReadBytes(ms);
                else _ = IcebergAvroOcfCodec.ReadLong(ms);
            }
        }
    }

    private static void SkipLongArray(Stream ms)
    {
        while (true)
        {
            var count = IcebergAvroOcfCodec.ReadLong(ms);
            if (count == 0) break;
            if (count < 0) { count = -count; _ = IcebergAvroOcfCodec.ReadLong(ms); }
            for (long i = 0; i < count; i++) _ = IcebergAvroOcfCodec.ReadLong(ms);
        }
    }

    private static int[] ReadIntArray(Stream ms)
    {
        var list = new List<int>();
        while (true)
        {
            var count = IcebergAvroOcfCodec.ReadLong(ms);
            if (count == 0) break;
            if (count < 0) { count = -count; _ = IcebergAvroOcfCodec.ReadLong(ms); }
            for (long i = 0; i < count; i++) list.Add(IcebergAvroOcfCodec.ReadInt(ms));
        }

        return list.ToArray();
    }

    private static string BuildCdcNameMappingJson()
    {
        var cols = new[]
        {
            "event_id", "stream_id", "stream_name", "source_schema", "source_table",
            "op", "lsn", "commit_timestamp_utc", "changed_columns", "after_json", "before_json",
            "business_key",
        };
        var items = cols.Select((name, idx) => new Dictionary<string, object>
        {
            ["field-id"] = idx + 1,
            ["names"] = new[] { name },
        });
        return JsonSerializer.Serialize(items);
    }

    [GeneratedRegex(@"commit_day=(\d{4}-\d{2}-\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex CommitDayRegex();
}
