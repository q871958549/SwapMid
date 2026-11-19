using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Parquet;
using Parquet.Schema;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

public class IcebergAvroManifestTests
{
    [Fact]
    public void AvroOcfCodec_Should_RoundTrip_Header_ZigZag_Numbers_Strings_And_SyncMarkers()
    {
        using var block = new MemoryStream();
        IcebergAvroOcfCodec.WriteInt(block, 0);
        IcebergAvroOcfCodec.WriteInt(block, -1);
        IcebergAvroOcfCodec.WriteInt(block, 123456);
        IcebergAvroOcfCodec.WriteLong(block, 0L);
        IcebergAvroOcfCodec.WriteLong(block, -9876543210123L);
        IcebergAvroOcfCodec.WriteLong(block, 876543210987654321L);
        IcebergAvroOcfCodec.WriteBool(block, true);
        IcebergAvroOcfCodec.WriteBool(block, false);
        IcebergAvroOcfCodec.WriteString(block, "s3://swapmid-lake/warehouse/orders/part_01.parquet");
        IcebergAvroOcfCodec.WriteNullableLong(block, null);
        IcebergAvroOcfCodec.WriteNullableLong(block, 42L);
        IcebergAvroOcfCodec.WriteNullableInt(block, null);
        IcebergAvroOcfCodec.WriteNullableInt(block, 7);

        var bytes = IcebergAvroOcfCodec.WriteSingleBlockFile(
            new Dictionary<string, string>
            {
                ["avro.schema"] = """{"type":"record","name":"t","fields":[]}""",
                ["format-version"] = "2",
            },
            recordCount: 1,
            encodedRecordsBlock: block.ToArray(),
            syncSeed: "swapmid-unit-test-seed");

        bytes.Take(4).ShouldBe(new byte[] { (byte)'O', (byte)'b', (byte)'j', 0x01 });

        var parsed = IcebergAvroOcfCodec.ReadFile(bytes);
        parsed.Metadata["avro.codec"].ShouldBe("null");
        parsed.Metadata["format-version"].ShouldBe("2");
        parsed.TotalRecordCount.ShouldBe(1);
        parsed.Blocks.Count.ShouldBe(1);

        using var readMs = new MemoryStream(parsed.Blocks[0], writable: false);
        IcebergAvroOcfCodec.ReadInt(readMs).ShouldBe(0);
        IcebergAvroOcfCodec.ReadInt(readMs).ShouldBe(-1);
        IcebergAvroOcfCodec.ReadInt(readMs).ShouldBe(123456);
        IcebergAvroOcfCodec.ReadLong(readMs).ShouldBe(0L);
        IcebergAvroOcfCodec.ReadLong(readMs).ShouldBe(-9876543210123L);
        IcebergAvroOcfCodec.ReadLong(readMs).ShouldBe(876543210987654321L);
        IcebergAvroOcfCodec.ReadBool(readMs).ShouldBeTrue();
        IcebergAvroOcfCodec.ReadBool(readMs).ShouldBeFalse();
        IcebergAvroOcfCodec.ReadString(readMs).ShouldBe("s3://swapmid-lake/warehouse/orders/part_01.parquet");
        IcebergAvroOcfCodec.ReadNullableLong(readMs).ShouldBeNull();
        IcebergAvroOcfCodec.ReadNullableLong(readMs).ShouldBe(42L);
        IcebergAvroOcfCodec.ReadNullableInt(readMs).ShouldBeNull();
        IcebergAvroOcfCodec.ReadNullableInt(readMs).ShouldBe(7);
        readMs.Position.ShouldBe(readMs.Length);
    }

    [Fact]
    public void ManifestFile_And_ManifestList_Avro_Should_RoundTrip_IcebergV2_Entries()
    {
        var manifestEntries = new List<IcebergAvroManifestBuilder.ManifestEntryRecord>
        {
            new(IcebergAvroManifestBuilder.StatusExisting, 1001L, 1L, 1L, "s3://lake/orders/run_1.parquet", "PARQUET", 50, 2048),
            new(IcebergAvroManifestBuilder.StatusAdded, 1002L, 2L, 2L, "s3://lake/orders/run_2.parquet", "PARQUET", 75, 4096),
            new(IcebergAvroManifestBuilder.StatusDeleted, 1002L, 2L, 1L, "s3://lake/orders/run_0.parquet", "PARQUET", 10, 512),
        };

        var manifestBytes = IcebergAvroManifestBuilder.BuildManifestFileAvro(
            """{"type":"struct","schema-id":0,"fields":[]}""",
            manifestEntries,
            "manifest-file-seed");

        var manifestOcf = IcebergAvroOcfCodec.ReadFile(manifestBytes);
        manifestOcf.Metadata["format-version"].ShouldBe("2");
        manifestOcf.Metadata["content"].ShouldBe("data");
        manifestOcf.Metadata["avro.schema"].ShouldContain("\"manifest_entry\"");
        manifestOcf.TotalRecordCount.ShouldBe(3);

        var decodedManifest = IcebergAvroManifestBuilder.ReadManifestFileAvro(manifestBytes);
        decodedManifest.Count.ShouldBe(3);
        decodedManifest[0].Status.ShouldBe(IcebergAvroManifestBuilder.StatusExisting);
        decodedManifest[1].Status.ShouldBe(IcebergAvroManifestBuilder.StatusAdded);
        decodedManifest[1].FilePath.ShouldBe("s3://lake/orders/run_2.parquet");
        decodedManifest[1].RecordCount.ShouldBe(75);
        decodedManifest[2].Status.ShouldBe(IcebergAvroManifestBuilder.StatusDeleted);

        var listEntries = new List<IcebergAvroManifestBuilder.ManifestListEntryRecord>
        {
            new(
                ManifestPath: "s3://lake/orders/metadata/run2-s2-m0.avro",
                ManifestLength: manifestBytes.Length,
                PartitionSpecId: 0,
                Content: 0,
                SequenceNumber: 2,
                MinSequenceNumber: 1,
                AddedSnapshotId: 1002L,
                AddedFilesCount: 1,
                ExistingFilesCount: 1,
                DeletedFilesCount: 1,
                AddedRowsCount: 75,
                ExistingRowsCount: 50,
                DeletedRowsCount: 10),
        };

        var listBytes = IcebergAvroManifestBuilder.BuildManifestListAvro(
            snapshotId: 1002L,
            parentSnapshotId: 1001L,
            sequenceNumber: 2L,
            manifests: listEntries,
            syncSeed: "manifest-list-seed");

        var listOcf = IcebergAvroOcfCodec.ReadFile(listBytes);
        listOcf.Metadata["format-version"].ShouldBe("2");
        listOcf.Metadata["snapshot-id"].ShouldBe("1002");
        listOcf.Metadata["parent-snapshot-id"].ShouldBe("1001");
        listOcf.Metadata["avro.schema"].ShouldContain("\"manifest_file\"");

        var decodedList = IcebergAvroManifestBuilder.ReadManifestListAvro(listBytes);
        decodedList.Count.ShouldBe(1);
        decodedList[0].ManifestPath.ShouldBe("s3://lake/orders/metadata/run2-s2-m0.avro");
        decodedList[0].ManifestLength.ShouldBe(manifestBytes.Length);
        decodedList[0].AddedRowsCount.ShouldBe(75);
        decodedList[0].DeletedFilesCount.ShouldBe(1);
    }

    [Fact]
    public async Task ObjectStorageEtlSinkWriter_Should_Embed_Parquet_FieldIds_1_To_11_And_TimestampMicros_With_IcebergV2_Avro()
    {
        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new MemoryS3Handler(objects);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.20",
            })
            .Build();

        var sink = new DataSource(
            Guid.NewGuid(),
            "minio-avro-test",
            DataSourceType.ObjectStorage,
            """
            {
              "provider": "minio",
              "endpoint": "http://198.51.100.20:9000",
              "region": "us-east-1",
              "bucket": "swapmid-lake",
              "prefix": "warehouse",
              "format": "parquet",
              "pathStyle": true,
              "accessKey": "ak",
              "secretKey": "sk"
            }
            """);

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-avro-parquet",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders/avro_check",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);
        stream.Activate();

        var writer = new ObjectStorageEtlSinkWriter(factory, config);
        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter>
        {
            [DataSourceType.ObjectStorage] = writer,
        };

        var commitUtc = new DateTime(2026, 11, 19, 12, 34, 56, DateTimeKind.Utc);
        var changes = new List<PgOutputCapturedChange>
        {
            new(
                "shop",
                "orders",
                "insert",
                "webhook:1",
                commitUtc,
                new Dictionary<string, object?> { ["order_id"] = 101, ["amount"] = 199.95 },
                null),
        };

        var delivered = await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink, changes, batchEndLsn: null, sinkWriters, CancellationToken.None,
            eventSequenceOffset: 0, idempotencyKeyOverride: "webhook:avro-test-1");
        delivered.ShouldBe(1);

        // 1. Verify Parquet Thrift FileMetaData embeds Iceberg FieldIds 1..11 and DateAndTimeMicros (UTC) for commit_timestamp_utc
        var parquetEntry = objects.Single(kv => kv.Key.EndsWith(".parquet", StringComparison.Ordinal));
        await using (var pqStream = new MemoryStream(parquetEntry.Value, writable: false))
        await using (var pqReader = await ParquetReader.CreateAsync(pqStream))
        {
            var fields = pqReader.Schema.DataFields;
            fields.Length.ShouldBe(12);
            var leafSchemaElements = pqReader.Metadata!.Schema.Skip(1).ToList();
            leafSchemaElements.Count.ShouldBe(12);
            for (var i = 0; i < 12; i++)
            {
                leafSchemaElements[i].Name.ShouldBe(fields[i].Name);
                leafSchemaElements[i].FieldId.ShouldBe(i + 1);
            }

            var tsField = fields.Single(f => f.Name == "commit_timestamp_utc").ShouldBeOfType<DateTimeDataField>();
            tsField.DateTimeFormat.ShouldBe(DateTimeFormat.DateAndTimeMicros);
            tsField.IsAdjustedToUTC.ShouldBeTrue();
        }

        // 2. Verify Iceberg v2 metadata points to .avro manifest-list + schema.name-mapping.default
        var metaBytes = objects["/swapmid-lake/warehouse/orders/avro_check/metadata/v1.metadata.json"];
        using (var doc = JsonDocument.Parse(metaBytes))
        {
            var props = doc.RootElement.GetProperty("properties");
            var nameMapping = props.GetProperty("schema.name-mapping.default").GetString().ShouldNotBeNull();
            nameMapping.ShouldContain("\"field-id\":1");
            nameMapping.ShouldContain("commit_timestamp_utc");

            var snap = doc.RootElement.GetProperty("snapshots")[0];
            var manifestListUri = snap.GetProperty("manifest-list").GetString()!;
            manifestListUri.ShouldEndWith(".avro");

            var mlKey = "/swapmid-lake/" + IcebergAvroManifestBuilder.StripS3BucketPrefix(manifestListUri, "swapmid-lake");
            objects.ContainsKey(mlKey).ShouldBeTrue();
            var listEntries = IcebergAvroManifestBuilder.ReadManifestListAvro(objects[mlKey]);
            listEntries.Count.ShouldBe(1);
            listEntries[0].ManifestPath.ShouldEndWith(".avro");

            var mfKey = "/swapmid-lake/" + IcebergAvroManifestBuilder.StripS3BucketPrefix(listEntries[0].ManifestPath, "swapmid-lake");
            objects.ContainsKey(mfKey).ShouldBeTrue();
            var fileEntries = IcebergAvroManifestBuilder.ReadManifestFileAvro(objects[mfKey]);
            fileEntries.Count.ShouldBe(1);
            fileEntries[0].RecordCount.ShouldBe(1);
            fileEntries[0].FileFormat.ShouldBe("PARQUET");
        }

        // 3. Verify zero-infra preview reads back timestamp_micros Parquet cleanly
        var historyReader = new DeltaLakeHistoryReader(factory, config);
        var previewService = new CdcLakehouseDataPreviewService(factory, config, historyReader);
        var preview = await previewService.PreviewAsync(stream, sink, requestedVersion: 1, maxRows: 10);
        preview.RowCount.ShouldBe(1);
        preview.Rows[0]["lsn"].ShouldBe("webhook:1");
        preview.Rows[0]["commit_timestamp_utc"].ShouldNotBeNull().ShouldContain("2026-11-19");
    }

    [Fact]
    public async Task DailyPartitioned_IcebergV2_Compaction_Should_Merge_Within_Same_Day_Partition_Only()
    {
        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new MemoryS3Handler(objects);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.20",
            })
            .Build();

        var sink = new DataSource(
            Guid.NewGuid(),
            "minio-part-compact",
            DataSourceType.ObjectStorage,
            """
            {
              "provider": "minio",
              "endpoint": "http://198.51.100.20:9000",
              "region": "us-east-1",
              "bucket": "swapmid-lake",
              "prefix": "warehouse",
              "format": "parquet",
              "pathStyle": true,
              "accessKey": "ak",
              "secretKey": "sk"
            }
            """);

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-daily-compact",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_daily",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders/part_compact",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);
        stream.ConfigureLakehousePartitionStrategy(LakehousePartitionStrategy.DailyCommitUtc);
        stream.Activate();

        var writer = new ObjectStorageEtlSinkWriter(factory, config);
        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter> { [DataSourceType.ObjectStorage] = writer };

        var d18 = new DateTime(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);
        var d19a = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
        var d19b = new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);

        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "webhook:1", d18, new Dictionary<string, object?> { ["id"] = 1 }, null),
                new PgOutputCapturedChange("shop", "orders", "insert", "webhook:2", d19a, new Dictionary<string, object?> { ["id"] = 2 }, null),
            ],
            null, sinkWriters, CancellationToken.None, 0, "webhook:batch-1");

        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "webhook:3", d19b, new Dictionary<string, object?> { ["id"] = 3 }, null),
            ],
            null, sinkWriters, CancellationToken.None, 2, "webhook:batch-2");

        var compactor = new CdcObjectStorageCompactionService(
            factory, config, Microsoft.Extensions.Logging.Abstractions.NullLogger<CdcObjectStorageCompactionService>.Instance);
        var result = await compactor.CompactAsync(stream, sink, minFiles: 2, deleteCompactedFiles: false, CancellationToken.None);
        result.Compacted.ShouldBeTrue();
        result.CompactedFileCount.ShouldBe(2);
        result.ConsolidatedRowCount.ShouldBe(2);
        result.OutputObjectKey.ShouldNotBeNull().ShouldContain("data/commit_day=2026-11-19/compact_");

        var historyReader = new DeltaLakeHistoryReader(factory, config);
        var histV3 = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        histV3.PartitionStrategy.ShouldBe(LakehousePartitionStrategy.DailyCommitUtc);
        histV3.CurrentVersion.ShouldBe(3);
        histV3.SelectedActiveFileCount.ShouldBe(2); // 1 file for 2026-11-18 + 1 compacted file for 2026-11-19
        histV3.SelectedTotalRecordCount.ShouldBe(3);
        histV3.ActiveFiles.Any(f => f.RelativePath.Contains("commit_day=2026-11-18", StringComparison.Ordinal)).ShouldBeTrue();
        histV3.ActiveFiles.Any(f => f.RelativePath.Contains("commit_day=2026-11-19/compact_", StringComparison.Ordinal)).ShouldBeTrue();
        histV3.TimeTravelQuerySql.ShouldNotBeNull().ShouldContain("WHERE commit_timestamp_utc >=");
    }

    [Fact]
    public void AwsSigV4Signer_Should_Not_Double_Encode_Hive_Partition_Equals_Segment()
    {
        var fixedTime = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var reqRaw = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri("http://minio:9000/swapmid-lake/orders/data/commit_day=2026-11-19/run_1.parquet"));
        var reqEncoded = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri("http://minio:9000/swapmid-lake/orders/data/commit_day%3D2026-11-19/run_1.parquet"));

        AwsSigV4Signer.Sign(reqRaw, "us-east-1", "minioadmin", "minioadmin", AwsSigV4Signer.EmptyPayloadSha256Hex, fixedTime);
        AwsSigV4Signer.Sign(reqEncoded, "us-east-1", "minioadmin", "minioadmin", AwsSigV4Signer.EmptyPayloadSha256Hex, fixedTime);

        var authRaw = reqRaw.Headers.GetValues("Authorization").Single();
        var authEncoded = reqEncoded.Headers.GetValues("Authorization").Single();
        authEncoded.ShouldBe(authRaw);
    }

    private sealed class MemoryS3Handler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _objects;
        public MemoryS3Handler(Dictionary<string, byte[]> objects) => _objects = objects;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                return _objects.TryGetValue(path, out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Put)
            {
                var hasIfNoneMatch = request.Headers.TryGetValues("If-None-Match", out var vals) &&
                                     string.Join(",", vals).Contains('*', StringComparison.Ordinal);
                if (hasIfNoneMatch && _objects.ContainsKey(path))
                {
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }

                _objects[path] = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }
    }
}
