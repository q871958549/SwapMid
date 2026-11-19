using System;
using System.Collections.Generic;
using System.IO;
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

public class IcebergV2CommitAndReaderTests
{
    private static IConfiguration BuildConfig()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.10",
            })
            .Build();

    private static DataSource BuildIcebergSink()
        => new(
            Guid.NewGuid(),
            "minio-iceberg",
            DataSourceType.ObjectStorage,
            """
            {
              "provider": "minio",
              "endpoint": "http://198.51.100.10:9000",
              "region": "us-east-1",
              "bucket": "swapmid-lake",
              "prefix": "warehouse",
              "format": "parquet",
              "pathStyle": true,
              "accessKey": "ak",
              "secretKey": "sk"
            }
            """);

    private static CdcStreamDefinition BuildIcebergStream(Guid sinkId)
        => new(
            Guid.NewGuid(),
            "orders-iceberg",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: sinkId,
            targetSinkDestination: "orders",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);

    private static async Task<byte[]> BuildParquetBytesAsync(string[] ops, long[] ids, string[] payloads)
    {
        var schema = new ParquetSchema(
            new DataField<string>("op"),
            new DataField<long>("id"),
            new DataField<string>("after_json"));

        await using var ms = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, ms))
        {
            using var rg = writer.CreateRowGroup();
            await rg.WriteAsync(schema.DataFields[0], ops);
            await rg.WriteAsync<long>(schema.DataFields[1], (ReadOnlyMemory<long>)ids);
            await rg.WriteAsync(schema.DataFields[2], payloads);
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task CommitAppend_And_ReplaceCompaction_Should_Produce_Valid_IcebergV2_Metadata_And_TimeTravel_Preview()
    {
        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new InMemoryIcebergS3Handler(objects);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var cfg = BuildConfig();
        var sink = BuildIcebergSink();
        var s3Cfg = ObjectStorageSinkConfigContract.ParseForWrite(sink.Config, requireSecretKey: true);
        var stream = BuildIcebergStream(sink.Id);
        var latestDoc = new CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument
        {
            StreamId = stream.Id,
            StreamName = stream.Name,
            Destination = "orders",
        };

        var p1Key = "warehouse/orders/run_1.parquet";
        var p2Key = "warehouse/orders/run_2.parquet";
        var pCompactKey = "warehouse/orders/compact_1.parquet";
        objects["/swapmid-lake/" + p1Key] = await BuildParquetBytesAsync(["insert", "insert"], [101L, 102L], ["{\"no\":\"A1\"}", "{\"no\":\"A2\"}"]);
        objects["/swapmid-lake/" + p2Key] = await BuildParquetBytesAsync(["update", "insert"], [101L, 103L], ["{\"no\":\"A1-U\"}", "{\"no\":\"A3\"}"]);
        objects["/swapmid-lake/" + pCompactKey] = await BuildParquetBytesAsync(["insert", "insert", "update", "insert"], [101L, 102L, 101L, 103L], ["{\"no\":\"A1\"}", "{\"no\":\"A2\"}", "{\"no\":\"A1-U\"}", "{\"no\":\"A3\"}"]);

        var r1 = await IcebergV2CommitWriter.CommitAppendAsync(
            factory, cfg, s3Cfg, "orders", stream, latestDoc,
            Guid.NewGuid(), p1Key, objects["/swapmid-lake/" + p1Key].Length, 2, false, DateTime.UtcNow, CancellationToken.None);
        r1.SequenceNumber.ShouldBe(1);
        latestDoc.LatestIcebergSequenceNumber = r1.SequenceNumber;
        latestDoc.LatestIcebergSnapshotId = r1.SnapshotId;
        latestDoc.LatestIcebergMetadataLocation = r1.MetadataLocationKey;

        var r2 = await IcebergV2CommitWriter.CommitAppendAsync(
            factory, cfg, s3Cfg, "orders", stream, latestDoc,
            Guid.NewGuid(), p2Key, objects["/swapmid-lake/" + p2Key].Length, 2, false, DateTime.UtcNow.AddSeconds(5), CancellationToken.None);
        r2.SequenceNumber.ShouldBe(2);
        latestDoc.LatestIcebergSequenceNumber = r2.SequenceNumber;

        var r3 = await IcebergV2CommitWriter.CommitReplaceCompactionAsync(
            factory, cfg, s3Cfg, "orders", stream, latestDoc,
            Guid.NewGuid(), [p1Key, p2Key], pCompactKey, objects["/swapmid-lake/" + pCompactKey].Length, 4, DateTime.UtcNow.AddSeconds(10), CancellationToken.None);
        r3.SequenceNumber.ShouldBe(3);

        Encoding.UTF8.GetString(objects["/swapmid-lake/warehouse/orders/metadata/version-hint.text"]).ShouldBe("3");
        using (var metaDoc = JsonDocument.Parse(objects["/swapmid-lake/warehouse/orders/metadata/v3.metadata.json"]))
        {
            metaDoc.RootElement.GetProperty("format-version").GetInt32().ShouldBe(2);
            metaDoc.RootElement.GetProperty("last-sequence-number").GetInt64().ShouldBe(3);
            metaDoc.RootElement.GetProperty("snapshots").GetArrayLength().ShouldBe(3);
            metaDoc.RootElement.GetProperty("metadata-log").GetArrayLength().ShouldBe(2);
        }

        var historyReader = new DeltaLakeHistoryReader(factory, cfg);
        var histV3 = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        histV3.TableFormat.ShouldBe(LakehouseTableFormat.ApacheIceberg);
        histV3.CurrentVersion.ShouldBe(3);
        histV3.SelectedVersion.ShouldBe(3);
        histV3.TotalCommitCount.ShouldBe(3);
        histV3.SelectedActiveFileCount.ShouldBe(1);
        histV3.SelectedTotalRecordCount.ShouldBe(4);
        histV3.Commits[0].Operation.ShouldBe("REPLACE");
        histV3.TimeTravelQuerySql.ShouldNotBeNull();
        histV3.TimeTravelQuerySql.ShouldContain("iceberg_scan");
        histV3.TimeTravelQuerySql.ShouldContain("FOR VERSION AS OF");

        var histV1 = await historyReader.ReadAsync(stream, sink, requestedVersion: 1);
        histV1.SelectedVersion.ShouldBe(1);
        histV1.SelectedActiveFileCount.ShouldBe(1);
        histV1.SelectedTotalRecordCount.ShouldBe(2);
        histV1.ActiveFiles[0].RelativePath.ShouldBe("run_1.parquet");

        var previewService = new CdcLakehouseDataPreviewService(factory, cfg, historyReader);
        var previewV1 = await previewService.PreviewAsync(stream, sink, requestedVersion: 1, maxRows: 10);
        previewV1.TableFormat.ShouldBe(LakehouseTableFormat.ApacheIceberg);
        previewV1.SelectedVersion.ShouldBe(1);
        previewV1.RowCount.ShouldBe(2);
        previewV1.Rows[0]["id"].ShouldBe("101");

        var previewV3 = await previewService.PreviewAsync(stream, sink, requestedVersion: 3, maxRows: 10);
        previewV3.SelectedVersion.ShouldBe(3);
        previewV3.RowCount.ShouldBe(4);
        previewV3.ScannedFileCount.ShouldBe(1);
    }

    [Fact]
    public async Task CommitAppend_Cas412_Race_And_Same_RunId_Replay_Should_Converge_Idempotently()
    {
        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new InMemoryIcebergS3Handler(objects) { ForceConflictOnV2Once = true };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var cfg = BuildConfig();
        var sink = BuildIcebergSink();
        var s3Cfg = ObjectStorageSinkConfigContract.ParseForWrite(sink.Config, requireSecretKey: true);
        var stream = BuildIcebergStream(sink.Id);
        var latestDoc = new CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument
        {
            StreamId = stream.Id,
            StreamName = stream.Name,
            Destination = "orders",
        };

        var r1 = await IcebergV2CommitWriter.CommitAppendAsync(
            factory, cfg, s3Cfg, "orders", stream, latestDoc,
            Guid.NewGuid(), "warehouse/orders/run_1.parquet", 512, 5, true, DateTime.UtcNow, CancellationToken.None);
        r1.SequenceNumber.ShouldBe(1);

        var runA = Guid.NewGuid();
        var rA = await IcebergV2CommitWriter.CommitAppendAsync(
            factory, cfg, s3Cfg, "orders", stream, latestDoc,
            runA, $"warehouse/orders/run_{runA:N}.parquet", 768, 7, false, DateTime.UtcNow, CancellationToken.None);
        rA.SequenceNumber.ShouldBe(3);

        // Stale caller replays runA -> detects its own swapmid-run-id at v3 and does NOT write v4!
        latestDoc.LatestIcebergSequenceNumber = 1;
        var replay = await IcebergV2CommitWriter.CommitAppendAsync(
            factory, cfg, s3Cfg, "orders", stream, latestDoc,
            runA, $"warehouse/orders/run_{runA:N}.parquet", 768, 7, false, DateTime.UtcNow, CancellationToken.None);
        replay.SequenceNumber.ShouldBe(3);
        replay.SnapshotId.ShouldBe(rA.SnapshotId);
        objects.ContainsKey("/swapmid-lake/warehouse/orders/metadata/v4.metadata.json").ShouldBeFalse();
    }

    private sealed class InMemoryIcebergS3Handler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _objects;
        public bool ForceConflictOnV2Once { get; set; }

        public InMemoryIcebergS3Handler(Dictionary<string, byte[]> objects)
        {
            _objects = objects;
        }

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
                if (ForceConflictOnV2Once && path.EndsWith("/metadata/v2.metadata.json", StringComparison.Ordinal))
                {
                    ForceConflictOnV2Once = false;
                    var competing = """
                        {
                          "format-version": 2,
                          "last-sequence-number": 2,
                          "current-snapshot-id": 9990002,
                          "snapshots": [
                            { "sequence-number": 2, "snapshot-id": 9990002, "timestamp-ms": 1763510000000, "summary": { "operation": "append", "swapmid-run-id": "competing-writer" } }
                          ],
                          "snapshot-log": [],
                          "metadata-log": []
                        }
                        """;
                    _objects[path] = Encoding.UTF8.GetBytes(competing);
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }

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
