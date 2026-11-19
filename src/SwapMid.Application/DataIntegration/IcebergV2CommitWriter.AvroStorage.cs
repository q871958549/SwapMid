using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using Volo.Abp;

namespace SwapMid.DataIntegration;

internal static partial class IcebergV2CommitWriter
{
    private static readonly string CdcIcebergSchemaCompactJson =
        BuildCdcIcebergSchemaNode().ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    internal static async Task UploadAvroManifestArtifactsAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string manifestFileKey,
        string manifestListKey,
        long snapshotId,
        long? parentSnapshotId,
        long nextSeq,
        Guid runId,
        IReadOnlyList<IcebergAvroManifestBuilder.ManifestEntryRecord> avroEntries,
        CancellationToken cancellationToken)
    {
        var manifestFileBytes = IcebergAvroManifestBuilder.BuildManifestFileAvro(
            CdcIcebergSchemaCompactJson,
            avroEntries,
            syncSeed: $"mf:{snapshotId}:{runId:N}:{nextSeq}");
        await PutBinaryObjectAsync(
            httpClientFactory, configuration, config, manifestFileKey, manifestFileBytes, "application/avro-binary", cancellationToken);

        var addedCount = avroEntries.Count(e => e.Status == IcebergAvroManifestBuilder.StatusAdded);
        var existingCount = avroEntries.Count(e => e.Status == IcebergAvroManifestBuilder.StatusExisting);
        var deletedCount = avroEntries.Count(e => e.Status == IcebergAvroManifestBuilder.StatusDeleted);
        var addedRows = avroEntries.Where(e => e.Status == IcebergAvroManifestBuilder.StatusAdded).Sum(e => e.RecordCount);
        var existingRows = avroEntries.Where(e => e.Status == IcebergAvroManifestBuilder.StatusExisting).Sum(e => e.RecordCount);
        var deletedRows = avroEntries.Where(e => e.Status == IcebergAvroManifestBuilder.StatusDeleted).Sum(e => e.RecordCount);
        var minSeq = avroEntries.Count > 0 ? avroEntries.Min(e => e.FileSequenceNumber) : nextSeq;

        var listEntry = new IcebergAvroManifestBuilder.ManifestListEntryRecord(
            ManifestPath: $"s3://{config.Bucket}/{manifestFileKey}",
            ManifestLength: manifestFileBytes.Length,
            PartitionSpecId: 0,
            Content: 0,
            SequenceNumber: nextSeq,
            MinSequenceNumber: minSeq,
            AddedSnapshotId: snapshotId,
            AddedFilesCount: addedCount,
            ExistingFilesCount: existingCount,
            DeletedFilesCount: deletedCount,
            AddedRowsCount: addedRows,
            ExistingRowsCount: existingRows,
            DeletedRowsCount: deletedRows);

        var manifestListBytes = IcebergAvroManifestBuilder.BuildManifestListAvro(
            snapshotId,
            parentSnapshotId,
            nextSeq,
            [listEntry],
            syncSeed: $"ml:{snapshotId}:{runId:N}:{nextSeq}");
        await PutBinaryObjectAsync(
            httpClientFactory, configuration, config, manifestListKey, manifestListBytes, "application/avro-binary", cancellationToken);
    }

    internal static async Task<List<IcebergManifestEntry>> LoadSnapshotActiveFilesAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        long snapshotId,
        string? manifestListUri,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(manifestListUri) &&
            manifestListUri.EndsWith(".avro", StringComparison.OrdinalIgnoreCase))
        {
            var listKey = IcebergAvroManifestBuilder.StripS3BucketPrefix(manifestListUri, config.Bucket);
            var listBytes = await TryGetObjectBytesAsync(httpClientFactory, configuration, config, listKey, cancellationToken);
            if (listBytes is { Length: > 0 })
            {
                try
                {
                    var manifests = IcebergAvroManifestBuilder.ReadManifestListAvro(listBytes);
                    var active = new Dictionary<string, IcebergManifestEntry>(StringComparer.Ordinal);
                    foreach (var manifest in manifests)
                    {
                        var mfKey = IcebergAvroManifestBuilder.StripS3BucketPrefix(manifest.ManifestPath, config.Bucket);
                        var mfBytes = await TryGetObjectBytesAsync(httpClientFactory, configuration, config, mfKey, cancellationToken);
                        if (mfBytes is not { Length: > 0 }) continue;

                        foreach (var entry in IcebergAvroManifestBuilder.ReadManifestFileAvro(mfBytes))
                        {
                            var objectKey = IcebergAvroManifestBuilder.StripS3BucketPrefix(entry.FilePath, config.Bucket);
                            var relPath = config.ToRelativeTablePath(destination, objectKey);
                            if (entry.Status == IcebergAvroManifestBuilder.StatusDeleted)
                            {
                                active.Remove(relPath);
                                continue;
                            }

                            active[relPath] = new IcebergManifestEntry
                            {
                                RelativePath = relPath,
                                ObjectKey = objectKey,
                                FilePath = entry.FilePath,
                                FileFormat = entry.FileFormat,
                                FileSizeInBytes = entry.FileSizeInBytes,
                                RecordCount = entry.RecordCount,
                                AddedAtSequence = entry.FileSequenceNumber > 0 ? entry.FileSequenceNumber : entry.SequenceNumber,
                            };
                        }
                    }

                    return active.Values
                        .OrderBy(f => f.AddedAtSequence)
                        .ThenBy(f => f.RelativePath, StringComparer.Ordinal)
                        .ToList();
                }
                catch (InvalidDataException)
                {
                    // Fall through to legacy JSON manifest if Avro payload is malformed.
                }
                catch (EndOfStreamException)
                {
                    // Fall through to legacy JSON manifest if Avro payload is truncated.
                }
            }
        }

        // Backward-compatible fallback for v2.3.0 JSON snapshot manifests.
        var jsonKey = !string.IsNullOrWhiteSpace(manifestListUri) && manifestListUri.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? IcebergAvroManifestBuilder.StripS3BucketPrefix(manifestListUri, config.Bucket)
            : config.BuildIcebergManifestKey(destination, snapshotId);
        var manifestJson = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
            httpClientFactory, configuration, config, jsonKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return new List<IcebergManifestEntry>();
        }

        try
        {
            var doc = JsonSerializer.Deserialize<IcebergSnapshotManifestDocument>(manifestJson, JsonOptions);
            return doc?.ActiveFiles ?? new List<IcebergManifestEntry>();
        }
        catch (JsonException)
        {
            return new List<IcebergManifestEntry>();
        }
    }

    private static async Task PutBinaryObjectAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string objectKey,
        byte[] bytes,
        string mediaType,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(objectKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, configuration, cancellationToken);

        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        content.Headers.ContentLength = bytes.Length;

        using var request = new HttpRequestMessage(HttpMethod.Put, objectUri) { Content = content };
        AwsSigV4Signer.Sign(
            request, config.Region, config.AccessKey, config.SecretKey ?? string.Empty, AwsSigV4Signer.ComputeSha256Hex(bytes));

        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", SinkConfigContractHelpers.RedactDiagnosticText($"Apache Iceberg Avro PUT 失败 ({(int)response.StatusCode}): {err}"));
        }
    }

    private static async Task<byte[]?> TryGetObjectBytesAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string objectKey,
        CancellationToken cancellationToken)
    {
        var objectUri = config.BuildObjectUri(objectKey);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(objectUri, configuration, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, objectUri);
        AwsSigV4Signer.Sign(
            request, config.Region, config.AccessKey, config.SecretKey ?? string.Empty, AwsSigV4Signer.EmptyPayloadSha256Hex);

        var client = httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
