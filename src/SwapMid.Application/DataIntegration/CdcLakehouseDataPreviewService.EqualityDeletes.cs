using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Parquet;
using Parquet.Schema;
using SwapMid.Monitoring;

namespace SwapMid.DataIntegration;

public sealed partial class CdcLakehouseDataPreviewService
{
    private async Task<CdcLakehousePreviewDto?> TryPreviewMergeOnReadIcebergAsync(
        CdcStreamDefinition stream,
        ObjectStorageSinkConfig config,
        string destination,
        long? requestedVersion,
        int boundedMaxRows,
        CancellationToken cancellationToken)
    {
        var (selectedVersion, entries) = await IcebergV2MetadataReader.ReadSnapshotEntriesForPreviewAsync(
            _httpClientFactory, _configuration, config, destination, requestedVersion, cancellationToken);
        var deleteEntries = entries
            .Where(e => e.Content == IcebergAvroManifestBuilder.ContentEqualityDeletes)
            .ToList();
        if (deleteEntries.Count == 0)
        {
            return null;
        }

        var dataEntries = entries
            .Where(e => e.Content == IcebergAvroManifestBuilder.ContentData)
            .OrderBy(e => e.AddedAtSequence)
            .ThenBy(e => e.RelativePath, StringComparer.Ordinal)
            .ToList();

        var deletedKeyMaxSeq = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var delEntry in deleteEntries)
        {
            var fetch = await TryFetchBoundedObjectBytesAsync(config, delEntry.ObjectKey, cancellationToken);
            if (fetch.Status != FetchStatus.Ok || fetch.Bytes == null) continue;
            var keys = await ReadEqualityDeleteKeysFromParquetAsync(fetch.Bytes, cancellationToken);
            foreach (var key in keys)
            {
                if (!deletedKeyMaxSeq.TryGetValue(key, out var existingSeq) || delEntry.AddedAtSequence > existingSeq)
                {
                    deletedKeyMaxSeq[key] = delEntry.AddedAtSequence;
                }
            }
        }

        var columns = new List<string>();
        var columnSet = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Dictionary<string, string?>>();
        var scannedFiles = new List<string>();
        var missingFiles = 0;
        var truncated = false;
        var filteredCount = 0;

        foreach (var dataEntry in dataEntries.Take(MaxFilesToScan))
        {
            if (rows.Count >= boundedMaxRows)
            {
                truncated = true;
                break;
            }

            var fetch = await TryFetchBoundedObjectBytesAsync(config, dataEntry.ObjectKey, cancellationToken);
            if (fetch.Status == FetchStatus.NotFound)
            {
                missingFiles++;
                continue;
            }

            if (fetch.Status == FetchStatus.TooLarge)
            {
                truncated = true;
                continue;
            }

            scannedFiles.Add(dataEntry.ObjectKey);
            var fileRows = new List<Dictionary<string, string?>>();
            var fileTruncated = await DecodeParquetAsync(
                fetch.Bytes!, boundedMaxRows + 200, columns, columnSet, fileRows, cancellationToken);
            if (fileTruncated) truncated = true;

            foreach (var row in fileRows)
            {
                if (row.TryGetValue("business_key", out var bKey) &&
                    !string.IsNullOrEmpty(bKey) &&
                    deletedKeyMaxSeq.TryGetValue(bKey, out var delSeq) &&
                    delSeq > dataEntry.AddedAtSequence)
                {
                    filteredCount++;
                    continue;
                }

                if (rows.Count >= boundedMaxRows)
                {
                    truncated = true;
                    break;
                }

                rows.Add(row);
            }
        }

        if (dataEntries.Count > MaxFilesToScan) truncated = true;

        PlatformMetrics.CdcLakehousePreviewsTotal.WithLabels(config.Format).Inc();
        var versionTag = selectedVersion.HasValue ? $"v{selectedVersion.Value}" : "latest";
        var summary =
            $"已加载 {versionTag} Merge-on-Read 样本 {rows.Count} 行（扫描 {scannedFiles.Count}/{dataEntries.Count} 个数据分片，应用 {deleteEntries.Count} 个 Equality Delete 分片并过滤 {filteredCount} 条过期/已删行{(truncated ? "，已截断" : string.Empty)}）。";

        return new CdcLakehousePreviewDto
        {
            StreamId = stream.Id,
            Destination = destination,
            TableFormat = stream.LakehouseTableFormat,
            FileFormat = config.Format,
            SelectedVersion = selectedVersion,
            ScannedFileCount = scannedFiles.Count,
            TotalActiveFileCount = dataEntries.Count,
            MissingFileCount = missingFiles,
            RowCount = rows.Count,
            Truncated = truncated,
            Columns = columns,
            Rows = rows,
            SourceFiles = scannedFiles,
            Summary = summary,
        };
    }

    internal static async Task<List<string>> ReadEqualityDeleteKeysFromParquetAsync(
        byte[] parquetBytes,
        CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        await using var mem = new MemoryStream(parquetBytes, writable: false);
        await using var reader = await ParquetReader.CreateAsync(mem, cancellationToken: cancellationToken);
        var keyField = reader.Schema.GetDataFields()
            .FirstOrDefault(f => string.Equals(f.Name, "business_key", StringComparison.OrdinalIgnoreCase));
        if (keyField == null) return keys;

        for (var rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rgReader = reader.OpenRowGroupReader(rg);
            var count = checked((int)rgReader.RowCount);
            if (count == 0) continue;
            var buf = new string?[count];
            await rgReader.ReadAsync(keyField, buf.AsMemory(), cancellationToken: cancellationToken);
            foreach (var v in buf)
            {
                if (!string.IsNullOrEmpty(v)) keys.Add(v);
            }
        }

        return keys;
    }

    private static string FormatScalar<T>(T value)
        where T : struct
        => value switch
        {
            DateTime dt => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private static string? FormatJsonElement(JsonElement el)
        => el.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => TruncateCell(el.GetString()),
            _ => TruncateCell(el.GetRawText()),
        };

    private static string? TruncateCell(string? value)
    {
        if (value == null || value.Length <= MaxCellChars)
        {
            return value;
        }

        return value[..MaxCellChars] + "…";
    }

    private enum FetchStatus { Ok, NotFound, TooLarge }
    private sealed record FetchResult(FetchStatus Status, byte[]? Bytes);
}
