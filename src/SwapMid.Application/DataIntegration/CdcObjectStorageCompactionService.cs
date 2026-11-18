using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Schema;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// CDC 对象存储（S3/MinIO Parquet &amp; JSONL）Lakehouse 小文件分片压缩合并服务：
/// 读取 _swapmid/latest.json，将多个增量小分片流式合并为单个紧凑 Parquet/JSONL 对象，
/// 并原子重写不可变 compaction manifest 与检查点索引，可选清理被合并源分片。
/// </summary>
public sealed class CdcObjectStorageCompactionService : ITransientDependency
{
    private const int MaxFilesPerCompaction = 50;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CdcObjectStorageCompactionService> _logger;

    public CdcObjectStorageCompactionService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CdcObjectStorageCompactionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    internal async Task<CdcLakehouseCompactionResultDto> CompactAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        int minFiles,
        bool deleteCompactedFiles,
        CancellationToken cancellationToken)
    {
        if (stream.TargetMode != CdcTargetMode.ExternalSink || targetSink.Type != DataSourceType.ObjectStorage)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅目标为 ObjectStorage（S3/MinIO）的 ExternalSink CDC 流支持 Lakehouse 分片压缩合并。");
        }

        if (minFiles is < CdcStreamConsts.MinLakehouseCompactMinFiles or > CdcStreamConsts.MaxLakehouseCompactMinFiles)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"最小合并分片阈值须在 {CdcStreamConsts.MinLakehouseCompactMinFiles}~{CdcStreamConsts.MaxLakehouseCompactMinFiles} 之间。");
        }

        var destination = stream.TargetSinkDestination?.Trim() ?? string.Empty;
        if (!ObjectStorageSinkConfigContract.IsValidDestinationPath(destination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "CDC 目标对象存储路径前缀缺失或格式不合法。");
        }

        var config = ObjectStorageSinkConfigContract.ParseForWrite(targetSink.Config, requireSecretKey: true);
        var latestKey = config.BuildLatestManifestKey(destination);
        var latestDoc = await CdcObjectStorageLakehouseWriter.LoadOrInitializeLatestAsync(
            _httpClientFactory,
            _configuration,
            config,
            latestKey,
            stream,
            destination,
            config.Format,
            cancellationToken);

        var candidates = latestDoc.RecentFiles.Take(MaxFilesPerCompaction).ToList();
        var nowUtc = DateTime.UtcNow;
        if (candidates.Count < minFiles)
        {
            PlatformMetrics.CdcLakehouseCompactionsTotal.WithLabels(config.Format, "skipped").Inc();
            return new CdcLakehouseCompactionResultDto
            {
                StreamId = stream.Id,
                Compacted = false,
                CompactedFileCount = 0,
                ConsolidatedRowCount = 0,
                CompletedAtUtc = nowUtc,
                Message = $"当前可合并分片数为 {candidates.Count}，未达到阈值（{minFiles}），已跳过合并。",
            };
        }

        try
        {
            return await ExecuteCompactionCoreAsync(
                stream,
                destination,
                config,
                latestKey,
                latestDoc,
                candidates,
                deleteCompactedFiles,
                nowUtc,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            PlatformMetrics.CdcLakehouseCompactionsTotal.WithLabels(config.Format, "failed").Inc();
            throw;
        }
    }

    internal async Task TryAutoCompactAsync(
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken)
    {
        if (stream.LakehouseCompactMinFiles is not int compactMin ||
            compactMin < CdcStreamConsts.MinLakehouseCompactMinFiles ||
            compactMin > CdcStreamConsts.MaxLakehouseCompactMinFiles ||
            targetSink is not { Type: DataSourceType.ObjectStorage } ||
            stream.TargetMode != CdcTargetMode.ExternalSink)
        {
            return;
        }

        try
        {
            await CompactAsync(
                stream,
                targetSink,
                compactMin,
                deleteCompactedFiles: true,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "CDC Lakehouse 自动压缩触发失败 streamId={StreamId} exceptionType={Type}",
                stream.Id, ex.GetType().FullName ?? ex.GetType().Name);
        }
    }

    private async Task<CdcLakehouseCompactionResultDto> ExecuteCompactionCoreAsync(
        CdcStreamDefinition stream,
        string destination,
        ObjectStorageSinkConfig config,
        string latestKey,
        CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument latestDoc,
        List<CdcObjectStorageLakehouseWriter.CdcLatestDataFileEntry> candidates,
        bool deleteCompactedFiles,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var fingerprint = $"compact:{candidates[0].DataObjectKey}..{candidates[^1].DataObjectKey}:{candidates.Count}";
        var compactRunId = CdcProtocolHelpers.BuildDeterministicPipelineRunId(stream.Id, fingerprint);
        var outputKey = config.BuildCompactedObjectKey(destination, compactRunId);
        var outputUri = config.BuildObjectUri(outputKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(outputUri, _configuration, cancellationToken);

        var tempOutPath = Path.Combine(Path.GetTempPath(), $"swapmid-compact-{compactRunId:N}.{config.Format}");
        try
        {
            if (config.Format == "jsonl")
            {
                await MergeJsonLinesAsync(candidates, config, tempOutPath, cancellationToken);
            }
            else
            {
                await MergeParquetFilesAsync(candidates, config, tempOutPath, cancellationToken);
            }

            var outputByteSize = new FileInfo(tempOutPath).Length;
            var sha256Hex = await ObjectStorageEtlSinkWriter.UploadFileAndComputeSha256Async(
                _httpClientFactory,
                tempOutPath,
                outputUri,
                config,
                cancellationToken);

            var endLsn = candidates.LastOrDefault(c => !string.IsNullOrWhiteSpace(c.EndLsn))?.EndLsn ?? latestDoc.CheckpointLsn;
            var startLsn = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.EndLsn))?.EndLsn ?? endLsn;
            var totalRows = candidates.Sum(c => c.RowCount);
            var lsnToken = CdcObjectStorageLakehouseWriter.BuildSafeLsnToken(endLsn ?? "0_0", isInitialSnapshot: false);
            var manifestKey = config.BuildCompactionManifestKey(destination, lsnToken, compactRunId);

            var manifest = new CdcObjectStorageLakehouseWriter.CdcBatchManifestDocument
            {
                StreamId = stream.Id,
                StreamName = stream.Name,
                RunId = compactRunId,
                IsInitialSnapshot = candidates.Any(c => c.IsInitialSnapshot),
                IsCompaction = true,
                CompactedSourceCount = candidates.Count,
                CompactedSourceKeys = candidates.Select(c => c.DataObjectKey).ToList(),
                Format = config.Format,
                DataObjectKey = outputKey,
                ByteSize = outputByteSize,
                Sha256Hex = sha256Hex,
                RowCount = totalRows,
                StartLsn = startLsn,
                EndLsn = endLsn,
                SchemaBaselineHash = stream.SchemaBaselineHash,
                MonitoredTables = stream.MonitoredTables,
                CommittedAtUtc = nowUtc,
            };

            await CdcObjectStorageLakehouseWriter.PutJsonObjectAsync(
                _httpClientFactory, _configuration, config, manifestKey,
                JsonSerializer.Serialize(manifest, CdcObjectStorageLakehouseWriter.JsonOptions),
                cancellationToken);

            var compactedSet = new HashSet<string>(candidates.Select(c => c.DataObjectKey), StringComparer.Ordinal);
            var remaining = latestDoc.RecentFiles.Where(f => !compactedSet.Contains(f.DataObjectKey)).ToList();
            remaining.Insert(0, new CdcObjectStorageLakehouseWriter.CdcLatestDataFileEntry
            {
                RunId = compactRunId,
                ManifestKey = manifestKey,
                DataObjectKey = outputKey,
                Format = config.Format,
                RowCount = totalRows,
                ByteSize = outputByteSize,
                Sha256Hex = sha256Hex,
                IsInitialSnapshot = candidates.Any(c => c.IsInitialSnapshot),
                IsCompaction = true,
                EndLsn = endLsn,
                CommittedAtUtc = nowUtc,
            });

            latestDoc.RecentFiles = remaining;
            latestDoc.StreamName = stream.Name;
            latestDoc.LatestManifestKey = manifestKey;
            latestDoc.LatestDataObjectKey = outputKey;
            latestDoc.CompactionCount += 1;
            latestDoc.LastCompactedAtUtc = nowUtc;
            latestDoc.UpdatedAtUtc = nowUtc;

            await CdcObjectStorageLakehouseWriter.PutJsonObjectAsync(
                _httpClientFactory,
                _configuration,
                config,
                latestKey,
                JsonSerializer.Serialize(latestDoc, CdcObjectStorageLakehouseWriter.JsonOptions),
                cancellationToken);

            var deletedCount = 0;
            if (deleteCompactedFiles)
            {
                foreach (var src in candidates)
                {
                    if (string.Equals(src.DataObjectKey, outputKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (await TryDeleteObjectAsync(config, src.DataObjectKey, cancellationToken))
                    {
                        deletedCount++;
                    }
                }
            }

            PlatformMetrics.CdcLakehouseCompactionsTotal.WithLabels(config.Format, "succeeded").Inc();
            PlatformMetrics.CdcLakehouseCompactedFilesTotal.WithLabels(config.Format).Inc(candidates.Count);
            PlatformMetrics.CdcObjectStorageManifestsTotal.WithLabels(config.Format).Inc();

            return new CdcLakehouseCompactionResultDto
            {
                StreamId = stream.Id,
                Compacted = true,
                CompactedFileCount = candidates.Count,
                ConsolidatedRowCount = totalRows,
                OutputObjectKey = outputKey,
                OutputManifestKey = manifestKey,
                OutputByteSize = outputByteSize,
                DeletedSourceCount = deletedCount,
                CompletedAtUtc = nowUtc,
                Message = $"已合并 {candidates.Count} 个 {config.Format.ToUpperInvariant()} 分片（共 {totalRows} 行，清理 {deletedCount} 个旧分片）。",
            };
        }
        finally
        {
            try
            {
                if (File.Exists(tempOutPath)) File.Delete(tempOutPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup of temp file.
            }
        }
    }

    private async Task MergeParquetFilesAsync(
        List<CdcObjectStorageLakehouseWriter.CdcLatestDataFileEntry> candidates,
        ObjectStorageSinkConfig config,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ParquetSchema? targetSchema = null;
        DataField[]? dataFields = null;
        ParquetWriter? writer = null;

        await using var outStream = new FileStream(
            outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        try
        {
            foreach (var candidate in candidates)
            {
                var bytes = await GetObjectBytesAsync(config, candidate.DataObjectKey, cancellationToken);
                await using var mem = new MemoryStream(bytes, writable: false);
                await using var reader = await ParquetReader.CreateAsync(mem, cancellationToken: cancellationToken);
                if (targetSchema == null)
                {
                    targetSchema = reader.Schema;
                    dataFields = targetSchema.GetDataFields();
                    writer = await ParquetWriter.CreateAsync(targetSchema, outStream, cancellationToken: cancellationToken);
                }

                for (var rg = 0; rg < reader.RowGroupCount; rg++)
                {
                    using var rgReader = reader.OpenRowGroupReader(rg);
                    var rowCount = checked((int)rgReader.RowCount);
                    if (rowCount == 0) continue;
                    using var rgWriter = writer!.CreateRowGroup();
                    foreach (var field in dataFields!)
                    {
                        await CopyParquetColumnAsync(rgReader, rgWriter, field, rowCount, cancellationToken);
                    }
                }
            }

            if (writer != null)
            {
                await writer.DisposeAsync();
                writer = null;
            }
        }
        finally
        {
            if (writer != null) await writer.DisposeAsync();
        }
    }

    private static async Task CopyParquetColumnAsync(
        ParquetRowGroupReader rgReader,
        ParquetRowGroupWriter rgWriter,
        DataField field,
        int rowCount,
        CancellationToken cancellationToken)
    {
        var clrType = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        if (clrType == typeof(string) || clrType == typeof(ReadOnlyMemory<char>))
        {
            var buf = new string?[rowCount];
            if (rgReader.ColumnExists(field))
            {
                await rgReader.ReadAsync(field, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync(field, buf);
            return;
        }

        if (clrType == typeof(byte[]) || clrType == typeof(ReadOnlyMemory<byte>))
        {
            var buf = new byte[]?[rowCount];
            if (rgReader.ColumnExists(field))
            {
                await rgReader.ReadAsync(field, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync(field, buf);
            return;
        }

        if (clrType == typeof(bool)) await CopyStructColumnAsync<bool>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(byte)) await CopyStructColumnAsync<byte>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(sbyte)) await CopyStructColumnAsync<sbyte>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(short)) await CopyStructColumnAsync<short>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(ushort)) await CopyStructColumnAsync<ushort>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(int)) await CopyStructColumnAsync<int>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(uint)) await CopyStructColumnAsync<uint>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(long)) await CopyStructColumnAsync<long>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(ulong)) await CopyStructColumnAsync<ulong>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(float)) await CopyStructColumnAsync<float>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(double)) await CopyStructColumnAsync<double>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(decimal)) await CopyStructColumnAsync<decimal>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateTime)) await CopyStructColumnAsync<DateTime>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateTimeOffset)) await CopyStructColumnAsync<DateTimeOffset>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateOnly)) await CopyStructColumnAsync<DateOnly>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(TimeOnly)) await CopyStructColumnAsync<TimeOnly>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(TimeSpan)) await CopyStructColumnAsync<TimeSpan>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(Guid)) await CopyStructColumnAsync<Guid>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", $"Parquet 合并暂不支持列类型 {field.Name} ({field.ClrType.Name})。");
        }
    }

    private static async Task CopyStructColumnAsync<T>(
        ParquetRowGroupReader rgReader,
        ParquetRowGroupWriter rgWriter,
        DataField field,
        int rowCount,
        CancellationToken cancellationToken)
        where T : struct
    {
        if (field.IsNullable)
        {
            var buf = new T?[rowCount];
            if (rgReader.ColumnExists(field))
            {
                await rgReader.ReadAsync<T>(field, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync<T>(field, (ReadOnlyMemory<T?>)buf, cancellationToken: cancellationToken);
        }
        else
        {
            var buf = new T[rowCount];
            if (rgReader.ColumnExists(field))
            {
                await rgReader.ReadAsync<T>(field, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync<T>(field, (ReadOnlyMemory<T>)buf, cancellationToken: cancellationToken);
        }
    }

    private async Task MergeJsonLinesAsync(
        List<CdcObjectStorageLakehouseWriter.CdcLatestDataFileEntry> candidates,
        ObjectStorageSinkConfig config,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using var outStream = new FileStream(
            outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        foreach (var candidate in candidates)
        {
            var bytes = await GetObjectBytesAsync(config, candidate.DataObjectKey, cancellationToken);
            await outStream.WriteAsync(bytes, cancellationToken);
            if (bytes.Length > 0 && bytes[^1] != (byte)'\n')
            {
                await outStream.WriteAsync(new[] { (byte)'\n' }, cancellationToken);
            }
        }
    }

    private async Task<byte[]> GetObjectBytesAsync(
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
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", $"读取 Lakehouse 分片对象 {objectKey} 失败 ({(int)response.StatusCode})。");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<bool> TryDeleteObjectAsync(
        ObjectStorageSinkConfig config,
        string objectKey,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(objectKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, _configuration, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Delete, objectUri);
        AwsSigV4Signer.Sign(
            request, config.Region, config.AccessKey, config.SecretKey ?? string.Empty, AwsSigV4Signer.EmptyPayloadSha256Hex);
        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent;
    }
}
