using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Schema;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// 零外部计算集群的 Lakehouse（S3/MinIO Parquet &amp; JSONL）实时数据预览服务：
/// 仅从权威 _delta_log / _swapmid/latest.json 解析合法分片路径，强制执行 SSRF 守卫、单文件 4 MiB 读预算与 50 行上限。
/// </summary>
public sealed class CdcLakehouseDataPreviewService : ITransientDependency
{
    internal const int MaxFilesToScan = 3;
    internal const int MaxBytesPerFile = 4 * 1024 * 1024;
    internal const int MaxCellChars = 2000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly DeltaLakeHistoryReader _deltaHistoryReader;

    public CdcLakehouseDataPreviewService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        DeltaLakeHistoryReader deltaHistoryReader)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _deltaHistoryReader = deltaHistoryReader;
    }

    public async Task<CdcLakehousePreviewDto> PreviewAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        long? requestedVersion = null,
        int maxRows = CdcStreamConsts.DefaultLakehousePreviewMaxRows,
        CancellationToken cancellationToken = default)
    {
        if (stream.TargetMode != CdcTargetMode.ExternalSink ||
            targetSink.Type != DataSourceType.ObjectStorage ||
            string.IsNullOrWhiteSpace(stream.TargetSinkDestination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅已绑定 ObjectStorage 接收端的 CDC 流支持 Lakehouse 实时数据预览。");
        }

        var config = ObjectStorageSinkConfigContract.ParseForWrite(targetSink.Config, requireSecretKey: false);
        var destination = stream.TargetSinkDestination.Trim();
        var boundedMaxRows = Math.Clamp(
            maxRows <= 0 ? CdcStreamConsts.DefaultLakehousePreviewMaxRows : maxRows,
            1,
            CdcStreamConsts.MaxLakehousePreviewMaxRows);

        var (selectedVersion, objectKeys) = await ResolveAuthoritativeObjectKeysAsync(
            stream,
            targetSink,
            config,
            destination,
            requestedVersion,
            cancellationToken);

        if (objectKeys.Count == 0)
        {
            return new CdcLakehousePreviewDto
            {
                StreamId = stream.Id,
                Destination = destination,
                TableFormat = stream.LakehouseTableFormat,
                FileFormat = config.Format,
                SelectedVersion = selectedVersion,
                Summary = selectedVersion.HasValue
                    ? $"{(stream.LakehouseTableFormat == LakehouseTableFormat.ApacheIceberg ? "Apache Iceberg" : "Delta Lake")} v{selectedVersion.Value} 当前无活跃数据分片可供预览。"
                    : "目标 Lakehouse 路径尚未写入任何数据分片，请先执行快照或触发捕获。",
            };
        }

        var columns = new List<string>();
        var columnSet = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Dictionary<string, string?>>();
        var scannedFiles = new List<string>();
        var missingFiles = 0;
        var truncated = false;

        foreach (var objectKey in objectKeys.Take(MaxFilesToScan))
        {
            if (rows.Count >= boundedMaxRows)
            {
                truncated = true;
                break;
            }

            var fetch = await TryFetchBoundedObjectBytesAsync(config, objectKey, cancellationToken);
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

            scannedFiles.Add(objectKey);
            var remaining = boundedMaxRows - rows.Count;
            var fileTruncated = config.Format == "jsonl"
                ? DecodeJsonLines(fetch.Bytes!, remaining, columns, columnSet, rows)
                : await DecodeParquetAsync(fetch.Bytes!, remaining, columns, columnSet, rows, cancellationToken);

            if (fileTruncated)
            {
                truncated = true;
            }
        }

        if (objectKeys.Count > MaxFilesToScan)
        {
            truncated = true;
        }

        PlatformMetrics.CdcLakehousePreviewsTotal.WithLabels(config.Format).Inc();

        var versionTag = selectedVersion.HasValue ? $"v{selectedVersion.Value}" : "latest";
        string summary;
        if (rows.Count == 0 && missingFiles > 0)
        {
            summary = $"版本 {versionTag} 的历史分片（{missingFiles} 个）已被物理清理或不可读；可切换至最新压缩版本或在合并时关闭删除旧分片。";
        }
        else if (rows.Count == 0 && truncated)
        {
            summary = $"版本 {versionTag} 分片超过单文件 4 MiB 在线预览预算，建议使用 DuckDB / Spark SQL 模板直接查询。";
        }
        else
        {
            summary = $"已加载 {versionTag} 样本 {rows.Count} 行（扫描 {scannedFiles.Count}/{objectKeys.Count} 个 {config.Format.ToUpperInvariant()} 分片{(truncated ? "，已截断" : string.Empty)}）。";
        }

        return new CdcLakehousePreviewDto
        {
            StreamId = stream.Id,
            Destination = destination,
            TableFormat = stream.LakehouseTableFormat,
            FileFormat = config.Format,
            SelectedVersion = selectedVersion,
            ScannedFileCount = scannedFiles.Count,
            TotalActiveFileCount = objectKeys.Count,
            MissingFileCount = missingFiles,
            RowCount = rows.Count,
            Truncated = truncated,
            Columns = columns,
            Rows = rows,
            SourceFiles = scannedFiles,
            Summary = summary,
        };
    }

    private async Task<(long? SelectedVersion, List<string> ObjectKeys)> ResolveAuthoritativeObjectKeysAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        ObjectStorageSinkConfig config,
        string destination,
        long? requestedVersion,
        CancellationToken cancellationToken)
    {
        if (stream.LakehouseTableFormat is LakehouseTableFormat.DeltaLake or LakehouseTableFormat.ApacheIceberg)
        {
            var history = await _deltaHistoryReader.ReadAsync(
                stream,
                targetSink,
                requestedVersion,
                maxCommits: CdcStreamConsts.MaxLakehouseHistoryMaxCommits,
                cancellationToken);
            var keys = history.ActiveFiles
                .Select(f => f.ObjectKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .ToList();
            return (history.SelectedVersion, keys);
        }

        var latestJson = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
            _httpClientFactory,
            _configuration,
            config,
            config.BuildLatestManifestKey(destination),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(latestJson))
        {
            return (null, new List<string>());
        }

        try
        {
            var doc = JsonSerializer.Deserialize<CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument>(
                latestJson,
                CdcObjectStorageLakehouseWriter.JsonOptions);
            var keys = (doc?.RecentFiles ?? new List<CdcObjectStorageLakehouseWriter.CdcLatestDataFileEntry>())
                .Select(f => f.DataObjectKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Reverse()
                .ToList();
            return (null, keys);
        }
        catch (JsonException)
        {
            return (null, new List<string>());
        }
    }

    private async Task<FetchResult> TryFetchBoundedObjectBytesAsync(
        ObjectStorageSinkConfig config,
        string objectKey,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(objectKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, _configuration, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, objectUri);
        AwsSigV4Signer.Sign(
            request, config.Region, config.AccessKey, config.SecretKey ?? string.Empty, AwsSigV4Signer.EmptyPayloadSha256Hex);
        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return new FetchResult(FetchStatus.NotFound, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", $"读取 Lakehouse 预览分片 {objectKey} 失败（HTTP {(int)response.StatusCode}）。");
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxBytesPerFile)
        {
            return new FetchResult(FetchStatus.TooLarge, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        var totalRead = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            totalRead += read;
            if (totalRead > MaxBytesPerFile)
            {
                return new FetchResult(FetchStatus.TooLarge, null);
            }

            buffer.Write(chunk, 0, read);
        }

        return new FetchResult(FetchStatus.Ok, buffer.ToArray());
    }

    internal static async Task<bool> DecodeParquetAsync(
        byte[] bytes,
        int maxRowsToAdd,
        List<string> columns,
        HashSet<string> columnSet,
        List<Dictionary<string, string?>> rows,
        CancellationToken cancellationToken)
    {
        await using var mem = new MemoryStream(bytes, writable: false);
        await using var reader = await ParquetReader.CreateAsync(mem, cancellationToken: cancellationToken);
        var dataFields = reader.Schema.GetDataFields();
        foreach (var field in dataFields)
        {
            if (columnSet.Add(field.Name))
            {
                columns.Add(field.Name);
            }
        }

        var added = 0;
        var truncated = false;
        for (var rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rgReader = reader.OpenRowGroupReader(rg);
            var groupRowCount = checked((int)rgReader.RowCount);
            if (groupRowCount == 0) continue;

            var takeCount = Math.Min(groupRowCount, maxRowsToAdd - added);
            if (takeCount < groupRowCount)
            {
                truncated = true;
            }

            if (takeCount <= 0)
            {
                truncated = true;
                break;
            }

            var columnValues = new Dictionary<string, string?[]>(dataFields.Length, StringComparer.Ordinal);
            foreach (var field in dataFields)
            {
                columnValues[field.Name] = await ReadParquetColumnPreviewStringsAsync(
                    rgReader, field, groupRowCount, takeCount, cancellationToken);
            }

            for (var i = 0; i < takeCount; i++)
            {
                var row = new Dictionary<string, string?>(dataFields.Length, StringComparer.Ordinal);
                foreach (var field in dataFields)
                {
                    row[field.Name] = columnValues[field.Name][i];
                }

                rows.Add(row);
                added++;
            }

            if (added >= maxRowsToAdd && rg < reader.RowGroupCount - 1)
            {
                truncated = true;
                break;
            }
        }

        return truncated;
    }

    internal static bool DecodeJsonLines(
        byte[] bytes,
        int maxRowsToAdd,
        List<string> columns,
        HashSet<string> columnSet,
        List<Dictionary<string, string?>> rows)
    {
        using var reader = new StringReader(Encoding.UTF8.GetString(bytes));
        var added = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (added >= maxRowsToAdd)
            {
                return true;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (columnSet.Add(prop.Name))
                    {
                        columns.Add(prop.Name);
                    }

                    row[prop.Name] = FormatJsonElement(prop.Value);
                }

                rows.Add(row);
                added++;
            }
            catch (JsonException)
            {
                // Skip malformed preview line.
            }
        }

        return false;
    }

    private static async Task<string?[]> ReadParquetColumnPreviewStringsAsync(
        ParquetRowGroupReader rgReader,
        DataField field,
        int groupRowCount,
        int takeCount,
        CancellationToken cancellationToken)
    {
        var output = new string?[takeCount];
        if (!rgReader.ColumnExists(field))
        {
            return output;
        }

        var clrType = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        if (clrType == typeof(string) || clrType == typeof(ReadOnlyMemory<char>))
        {
            var buf = new string?[groupRowCount];
            await rgReader.ReadAsync(field, buf.AsMemory(), cancellationToken: cancellationToken);
            for (var i = 0; i < takeCount; i++) output[i] = TruncateCell(buf[i]);
            return output;
        }

        if (clrType == typeof(byte[]) || clrType == typeof(ReadOnlyMemory<byte>))
        {
            var buf = new byte[]?[groupRowCount];
            await rgReader.ReadAsync(field, buf.AsMemory(), cancellationToken: cancellationToken);
            for (var i = 0; i < takeCount; i++) output[i] = buf[i] == null ? null : $"0x{Convert.ToHexString(buf[i]!.AsSpan(0, Math.Min(buf[i]!.Length, 32)))}";
            return output;
        }

        if (clrType == typeof(bool)) return await ReadStructColumnStringsAsync<bool>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(byte)) return await ReadStructColumnStringsAsync<byte>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(sbyte)) return await ReadStructColumnStringsAsync<sbyte>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(short)) return await ReadStructColumnStringsAsync<short>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(ushort)) return await ReadStructColumnStringsAsync<ushort>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(int)) return await ReadStructColumnStringsAsync<int>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(uint)) return await ReadStructColumnStringsAsync<uint>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(long)) return await ReadStructColumnStringsAsync<long>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(ulong)) return await ReadStructColumnStringsAsync<ulong>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(float)) return await ReadStructColumnStringsAsync<float>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(double)) return await ReadStructColumnStringsAsync<double>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(decimal)) return await ReadStructColumnStringsAsync<decimal>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(DateTime)) return await ReadStructColumnStringsAsync<DateTime>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(DateTimeOffset)) return await ReadStructColumnStringsAsync<DateTimeOffset>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(DateOnly)) return await ReadStructColumnStringsAsync<DateOnly>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(TimeOnly)) return await ReadStructColumnStringsAsync<TimeOnly>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(TimeSpan)) return await ReadStructColumnStringsAsync<TimeSpan>(rgReader, field, groupRowCount, takeCount, cancellationToken);
        if (clrType == typeof(Guid)) return await ReadStructColumnStringsAsync<Guid>(rgReader, field, groupRowCount, takeCount, cancellationToken);

        return output;
    }

    private static async Task<string?[]> ReadStructColumnStringsAsync<T>(
        ParquetRowGroupReader rgReader,
        DataField field,
        int groupRowCount,
        int takeCount,
        CancellationToken cancellationToken)
        where T : struct
    {
        var output = new string?[takeCount];
        if (field.IsNullable)
        {
            var buf = new T?[groupRowCount];
            await rgReader.ReadAsync<T>(field, buf.AsMemory(), cancellationToken: cancellationToken);
            for (var i = 0; i < takeCount; i++) output[i] = buf[i].HasValue ? FormatScalar(buf[i]!.Value) : null;
        }
        else
        {
            var buf = new T[groupRowCount];
            await rgReader.ReadAsync<T>(field, buf.AsMemory(), cancellationToken: cancellationToken);
            for (var i = 0; i < takeCount; i++) output[i] = FormatScalar(buf[i]);
        }

        return output;
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
