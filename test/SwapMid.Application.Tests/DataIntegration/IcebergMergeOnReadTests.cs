using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SwapMid.Infrastructure;
using Xunit;

namespace SwapMid.DataIntegration;

public class IcebergMergeOnReadTests
{
    [Fact]
    public async Task MergeOnReadUpsert_Should_Emit_EqualityDeletes_Deduplicate_Preview_And_Materialize_On_Compaction()
    {
        var store = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new MemoryS3Handler(store);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.30",
            })
            .Build();

        var sink = new DataSource(
            Guid.NewGuid(),
            "minio-mor",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"http://198.51.100.30:9000","region":"us-east-1","bucket":"lake","prefix":"mor","format":"parquet","pathStyle":true,"accessKey":"ak","secretKey":"sk"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-mor-upsert",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_orders_mor",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders/upsert",
            targetSinkKeyColumns: "order_id",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);
        stream.ConfigureLakehouseWriteMode(LakehouseWriteMode.MergeOnReadUpsert);

        var sinkWriter = new ObjectStorageEtlSinkWriter(factory, config);
        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter> { [DataSourceType.ObjectStorage] = sinkWriter };

        var t1 = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 19, 11, 0, 0, DateTimeKind.Utc);

        // Batch 1 (v1): insert 25001 (120.5) & 25002 (240.0)
        var d1 = await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "webhook:1", t1,
                    new Dictionary<string, object?> { ["order_id"] = 25001, ["amount"] = 120.5m }, null),
                new PgOutputCapturedChange("shop", "orders", "insert", "webhook:2", t1,
                    new Dictionary<string, object?> { ["order_id"] = 25002, ["amount"] = 240.0m }, null),
            ],
            null, sinkWriters, CancellationToken.None, 0, "webhook:batch-1");
        d1.ShouldBe(2);

        // Batch 2 (v2): update 25001 (125.0), delete 25002, insert 25003 (360.75)
        var d2 = await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "update", "webhook:3", t2,
                    new Dictionary<string, object?> { ["order_id"] = 25001, ["amount"] = 125.0m },
                    new Dictionary<string, object?> { ["order_id"] = 25001, ["amount"] = 120.5m }),
                new PgOutputCapturedChange("shop", "orders", "delete", "webhook:4", t2,
                    null,
                    new Dictionary<string, object?> { ["order_id"] = 25002, ["amount"] = 240.0m }),
                new PgOutputCapturedChange("shop", "orders", "insert", "webhook:5", t2,
                    new Dictionary<string, object?> { ["order_id"] = 25003, ["amount"] = 360.75m }, null),
            ],
            null, sinkWriters, CancellationToken.None, 2, "webhook:batch-2");
        d2.ShouldBe(3);

        // Verify v2 metadata has both data manifest (*-m0.avro) and delete manifest (*-d0.avro) + equality delete parquet
        store.Keys.Any(k => k.EndsWith("-d0.avro", StringComparison.Ordinal)).ShouldBeTrue();
        store.Keys.Any(k => k.Contains("/eqdel_", StringComparison.Ordinal) && k.EndsWith(".parquet", StringComparison.Ordinal)).ShouldBeTrue();

        var historyReader = new DeltaLakeHistoryReader(factory, config);
        var histV2 = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        histV2.WriteMode.ShouldBe(LakehouseWriteMode.MergeOnReadUpsert);
        histV2.CurrentVersion.ShouldBe(2);
        histV2.SelectedActiveFileCount.ShouldBe(2);
        histV2.SelectedEqualityDeleteFileCount.ShouldBe(1);
        histV2.SelectedEqualityDeleteRecordCount.ShouldBe(3); // 25001, 25002, 25003
        histV2.Commits[0].Operation.ShouldBe("OVERWRITE");

        // Zero-Infra Preview at v2 must apply Equality Deletes -> exact 2 live rows (25001=125.0, 25003=360.75), NO 25002!
        var previewSvc = new CdcLakehouseDataPreviewService(factory, config, historyReader);
        var previewV2 = await previewSvc.PreviewAsync(stream, sink, requestedVersion: 2, maxRows: 20);
        previewV2.RowCount.ShouldBe(2);
        previewV2.Rows.Select(r => r["business_key"]).OrderBy(x => x).ShouldBe(["shop.orders:order_id=25001", "shop.orders:order_id=25003"]);
        previewV2.Rows.Single(r => r["business_key"] == "shop.orders:order_id=25001")["after_json"].ShouldNotBeNull().ShouldContain("125");

        // Historical Time-Travel Preview at v1 must STILL show original 2 rows (25001=120.5, 25002=240.0)
        var previewV1 = await previewSvc.PreviewAsync(stream, sink, requestedVersion: 1, maxRows: 20);
        previewV1.RowCount.ShouldBe(2);
        previewV1.Rows.Select(r => r["business_key"]).OrderBy(x => x).ShouldBe(["shop.orders:order_id=25001", "shop.orders:order_id=25002"]);

        // Compaction (v3 REPLACE) should materialize live 2 rows AND remove superseded equality delete file!
        var compactor = new CdcObjectStorageCompactionService(factory, config, NullLogger<CdcObjectStorageCompactionService>.Instance);
        var compactResult = await compactor.CompactAsync(stream, sink, minFiles: 2, deleteCompactedFiles: false, CancellationToken.None);
        compactResult.Compacted.ShouldBeTrue();
        compactResult.ConsolidatedRowCount.ShouldBe(2);

        var histV3 = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        histV3.CurrentVersion.ShouldBe(3);
        histV3.SelectedActiveFileCount.ShouldBe(1);
        histV3.SelectedTotalRecordCount.ShouldBe(2);
        histV3.SelectedEqualityDeleteFileCount.ShouldBe(0);

        var previewV3 = await previewSvc.PreviewAsync(stream, sink, requestedVersion: 3, maxRows: 20);
        previewV3.RowCount.ShouldBe(2);
        previewV3.Rows.Select(r => r["business_key"]).OrderBy(x => x).ShouldBe(["shop.orders:order_id=25001", "shop.orders:order_id=25003"]);
    }

    private sealed class MemoryS3Handler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _objects;
        public MemoryS3Handler(Dictionary<string, byte[]> objects) => _objects = objects;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
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
