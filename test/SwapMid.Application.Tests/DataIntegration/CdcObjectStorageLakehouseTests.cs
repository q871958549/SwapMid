using System;
using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging.Abstractions;
using NpgsqlTypes;
using Parquet;
using Shouldly;
using SwapMid.Infrastructure;
using Xunit;

namespace SwapMid.DataIntegration;

public class CdcObjectStorageLakehouseTests
{
    [Fact]
    public async Task DeliverAsync_Should_Write_BatchManifest_Bounded_LatestIndex_And_Probe_Into_Template()
    {
        var bucketObjects = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new InMemoryS3Handler(bucketObjects);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedHosts] = "8.8.8.8",
            })
            .Build();

        var s3Sink = new DataSource(
            Guid.NewGuid(),
            "minio-lakehouse",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"https://8.8.8.8:9000","region":"us-east-1","bucket":"lakehouse","prefix":"cdc","format":"parquet","accessKey":"AK","secretKey":"SK","targetResourceGroup":"rg-s3"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-lakehouse-stream",
            Guid.NewGuid(),
            "pub_orders",
            "slot_orders",
            CdcTargetMode.ExternalSink,
            monitoredTables: "public.orders",
            targetSinkId: s3Sink.Id,
            targetSinkDestination: "orders/raw");

        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter>
        {
            [DataSourceType.ObjectStorage] = new ObjectStorageEtlSinkWriter(factory, config),
        };

        var t0 = new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
        IReadOnlyList<PgOutputCapturedChange> batch1 =
        [
            new("public", "orders", "insert", "0/100", t0, new Dictionary<string, object?> { ["order_id"] = 1L, ["amount"] = 50 }, null),
            new("public", "orders", "update", "0/101", t0.AddSeconds(1), new Dictionary<string, object?> { ["order_id"] = 1L, ["amount"] = 75 }, null),
        ];

        var delivered1 = await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream,
            s3Sink,
            batch1,
            NpgsqlLogSequenceNumber.Parse("0/101"),
            sinkWriters,
            CancellationToken.None);
        delivered1.ShouldBe(2);

        const string latestHttpPath = "/lakehouse/cdc/orders/raw/_swapmid/latest.json";
        bucketObjects.Keys.ShouldContain(k => k.StartsWith("/lakehouse/cdc/orders/raw/_swapmid/manifests/0_101_", StringComparison.Ordinal) && k.EndsWith(".json", StringComparison.Ordinal));
        bucketObjects.Keys.ShouldContain(latestHttpPath);

        var latestDoc1 = JsonDocument.Parse(Encoding.UTF8.GetString(bucketObjects[latestHttpPath])).RootElement;
        latestDoc1.GetProperty("checkpointLsn").GetString().ShouldBe("0/101");
        latestDoc1.GetProperty("format").GetString().ShouldBe("parquet");
        latestDoc1.GetProperty("recentFiles").GetArrayLength().ShouldBe(1);
        latestDoc1.GetProperty("recentFiles")[0].GetProperty("rowCount").GetInt64().ShouldBe(2);

        // Emit 104 more distinct batches -> total 105 commits, latest.json recentFiles must remain bounded at 100
        for (var i = 2; i <= 105; i++)
        {
            var lsn = $"0/{100 + i:X}";
            var deliveredN = await CdcExternalSinkDeliveryAdapter.DeliverAsync(
                stream,
                s3Sink,
                [new PgOutputCapturedChange("public", "orders", "insert", lsn, t0.AddSeconds(i), new Dictionary<string, object?> { ["order_id"] = (long)i }, null)],
                NpgsqlLogSequenceNumber.Parse(lsn),
                sinkWriters,
                CancellationToken.None);
            deliveredN.ShouldBe(1);
        }

        var latestDoc105 = JsonDocument.Parse(Encoding.UTF8.GetString(bucketObjects[latestHttpPath])).RootElement;
        latestDoc105.GetProperty("recentFiles").GetArrayLength().ShouldBe(100);
        latestDoc105.GetProperty("checkpointLsn").GetString().ShouldBe($"0/{100 + 105:X}");

        // Live template probe reads back latest.json into CdcSinkTemplateDto
        var probeService = new DataIntegrationSinkProbeService(factory, config);
        var template = await CdcExternalSinkTemplateBuilder.BuildAsync(stream, s3Sink, probeService, CancellationToken.None);

        template.ManifestDirectory.ShouldBe("cdc/orders/raw/_swapmid/manifests");
        template.ManifestLatestKey.ShouldBe("cdc/orders/raw/_swapmid/latest.json");
        template.TargetExists.ShouldBe(true);
        template.LatestManifestJson.ShouldNotBeNullOrWhiteSpace();
        template.LatestManifestJson.ShouldContain("orders-lakehouse-stream");
        template.LakehouseQuerySql.ShouldNotBeNullOrWhiteSpace();
        template.LakehouseQuerySql.ShouldContain("read_parquet");
    }

    [Fact]
    public async Task CompactAsync_Should_Merge_Parquet_RowGroups_Update_LatestIndex_And_Delete_Source_Fragments()
    {
        var bucketObjects = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new InMemoryS3Handler(bucketObjects);
        var factory = new SingleHandlerHttpClientFactory(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedHosts] = "8.8.8.8",
            })
            .Build();

        var s3Sink = new DataSource(
            Guid.NewGuid(),
            "minio-compact",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"https://8.8.8.8:9000","region":"us-east-1","bucket":"lakehouse","prefix":"cdc","format":"parquet","accessKey":"AK","secretKey":"SK","targetResourceGroup":"rg-s3"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-compact-stream",
            Guid.NewGuid(),
            "pub_compact",
            "slot_compact",
            CdcTargetMode.ExternalSink,
            monitoredTables: "public.orders",
            targetSinkId: s3Sink.Id,
            targetSinkDestination: "orders/compact",
            lakehouseCompactMinFiles: 2);

        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter>
        {
            [DataSourceType.ObjectStorage] = new ObjectStorageEtlSinkWriter(factory, config),
        };

        var t0 = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 3; i++)
        {
            var lsn = $"0/{200 + i:X}";
            await CdcExternalSinkDeliveryAdapter.DeliverAsync(
                stream,
                s3Sink,
                [new PgOutputCapturedChange("public", "orders", "insert", lsn, t0.AddSeconds(i), new Dictionary<string, object?> { ["order_id"] = (long)i }, null)],
                NpgsqlLogSequenceNumber.Parse(lsn),
                sinkWriters,
                CancellationToken.None);
        }

        bucketObjects.Keys.Count(k => k.EndsWith(".parquet", StringComparison.Ordinal)).ShouldBe(3);

        var compactor = new CdcObjectStorageCompactionService(factory, config, NullLogger<CdcObjectStorageCompactionService>.Instance);
        var result = await compactor.CompactAsync(stream, s3Sink, minFiles: 2, deleteCompactedFiles: true, CancellationToken.None);

        result.Compacted.ShouldBeTrue();
        result.CompactedFileCount.ShouldBe(3);
        result.ConsolidatedRowCount.ShouldBe(3);
        result.DeletedSourceCount.ShouldBe(3);
        result.OutputObjectKey.ShouldNotBeNullOrWhiteSpace();
        result.OutputManifestKey.ShouldNotBeNullOrWhiteSpace();

        // Only the 1 consolidated compact_*.parquet object should remain
        var remainingParquetKeys = bucketObjects.Keys.Where(k => k.EndsWith(".parquet", StringComparison.Ordinal)).ToList();
        remainingParquetKeys.Count.ShouldBe(1);
        remainingParquetKeys[0].ShouldBe($"/lakehouse/{result.OutputObjectKey}");

        await using (var verifyMem = new MemoryStream(bucketObjects[remainingParquetKeys[0]], writable: false))
        await using (var pqReader = await ParquetReader.CreateAsync(verifyMem))
        {
            pqReader.RowGroupCount.ShouldBe(3);
            long rows = 0;
            for (var rg = 0; rg < pqReader.RowGroupCount; rg++)
            {
                using var rgr = pqReader.OpenRowGroupReader(rg);
                rows += rgr.RowCount;
            }

            rows.ShouldBe(3);
        }

        var latestJson = Encoding.UTF8.GetString(bucketObjects["/lakehouse/cdc/orders/compact/_swapmid/latest.json"]);
        var latestDoc = JsonDocument.Parse(latestJson).RootElement;
        latestDoc.GetProperty("compactionCount").GetInt32().ShouldBe(1);
        latestDoc.GetProperty("recentFiles").GetArrayLength().ShouldBe(1);
        latestDoc.GetProperty("recentFiles")[0].GetProperty("isCompaction").GetBoolean().ShouldBeTrue();

        // Repeating compaction when fewer than threshold should skip cleanly
        var second = await compactor.CompactAsync(stream, s3Sink, minFiles: 2, deleteCompactedFiles: true, CancellationToken.None);
        second.Compacted.ShouldBeFalse();
        second.CompactedFileCount.ShouldBe(0);
    }

    private sealed class InMemoryS3Handler(ConcurrentDictionary<string, byte[]> objects) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                var payload = request.Content != null
                    ? await request.Content.ReadAsByteArrayAsync(cancellationToken)
                    : Array.Empty<byte>();
                objects[path] = payload;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
            }

            if (request.Method == HttpMethod.Delete)
            {
                objects.TryRemove(path, out _);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Get)
            {
                if (objects.TryGetValue(path, out var existing))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(existing),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }
    }

    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
