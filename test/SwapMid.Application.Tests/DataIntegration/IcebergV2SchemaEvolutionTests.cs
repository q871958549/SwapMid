using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Parquet;
using Shouldly;
using SwapMid.Infrastructure;
using Volo.Abp;
using Xunit;
using Xunit.Abstractions;

namespace SwapMid.DataIntegration;

public class IcebergV2SchemaEvolutionTests
{
    private readonly ITestOutputHelper _output;

    public IcebergV2SchemaEvolutionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task EvolveSchema_Should_Add_Rename_Drop_With_Immutable_FieldIds_And_Preserve_Through_Compaction()
    {
        var store = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new MemoryS3Handler(store);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.35",
            })
            .Build();

        var sink = new DataSource(
            Guid.NewGuid(),
            "minio-schema-evolve",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"http://198.51.100.35:9000","region":"us-east-1","bucket":"lake","prefix":"evolve","format":"parquet","pathStyle":true,"accessKey":"ak","secretKey":"sk"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-iceberg-schema",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_orders_schema",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders/schema",
            targetSinkKeyColumns: "order_id",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);

        var sinkWriter = new ObjectStorageEtlSinkWriter(factory, config);
        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter> { [DataSourceType.ObjectStorage] = sinkWriter };
        var schemaSvc = new IcebergV2SchemaEvolutionService(factory, config);
        var historyReader = new DeltaLakeHistoryReader(factory, config);
        var t1 = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

        // v1: initial schema-id 0 (FieldId 1..12)
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "s:1", t1,
                    new Dictionary<string, object?> { ["order_id"] = 30001, ["order_amount"] = 100.25, ["currency"] = "USD" }, null),
            ],
            null, sinkWriters, CancellationToken.None, 0, "se:b1");

        // Reject core envelope drop/rename
        await Should.ThrowAsync<BusinessException>(async () =>
            await schemaSvc.EvolveSchemaAsync(stream, sink, new EvolveCdcLakehouseSchemaInput
            {
                DropColumns = ["event_id"],
                DryRun = false,
            }));
        await Should.ThrowAsync<BusinessException>(async () =>
            await schemaSvc.EvolveSchemaAsync(stream, sink, new EvolveCdcLakehouseSchemaInput
            {
                RenameColumns = [new RenameCdcLakehouseColumnInput { FromName = "business_key", ToName = "pk" }],
                DryRun = false,
            }));

        // DryRun add 3 typed columns -> zero metadata mutation
        var dryAdd = await schemaSvc.EvolveSchemaAsync(stream, sink, new EvolveCdcLakehouseSchemaInput
        {
            AddColumns =
            [
                new AddCdcLakehouseColumnInput { Name = "order_amount", Type = "double", Doc = "Order total" },
                new AddCdcLakehouseColumnInput { Name = "currency", Type = "string" },
                new AddCdcLakehouseColumnInput { Name = "is_vip", Type = "boolean" },
            ],
            DryRun = true,
        });
        dryAdd.DryRun.ShouldBeTrue();
        dryAdd.PreviousSchemaId.ShouldBe(0);
        dryAdd.NewSchemaId.ShouldBe(1);
        dryAdd.LastColumnId.ShouldBe(15);
        dryAdd.MetadataVersion.ShouldBe(1);

        // Apply add -> metadata v2, schema-id 1, FieldId 13..15
        var added = await schemaSvc.EvolveSchemaAsync(stream, sink, new EvolveCdcLakehouseSchemaInput
        {
            AddColumns =
            [
                new AddCdcLakehouseColumnInput { Name = "order_amount", Type = "double", Doc = "Order total" },
                new AddCdcLakehouseColumnInput { Name = "currency", Type = "string" },
                new AddCdcLakehouseColumnInput { Name = "is_vip", Type = "boolean" },
            ],
            DryRun = false,
        });
        added.DryRun.ShouldBeFalse();
        added.NewSchemaId.ShouldBe(1);
        added.LastColumnId.ShouldBe(15);
        added.MetadataVersion.ShouldBe(2);
        added.ActiveFields.ShouldContain(f => f.FieldId == 13 && f.Name == "order_amount" && f.Type == "double" && !f.IsCoreEnvelope);
        added.ActiveFields.ShouldContain(f => f.FieldId == 14 && f.Name == "currency" && f.Type == "string");
        added.ActiveFields.ShouldContain(f => f.FieldId == 15 && f.Name == "is_vip" && f.Type == "boolean");

        // Commit v3 under schema-id 1: Parquet MUST embed FieldId 13, 14, 15 and typed values!
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "s:2", t2,
                    new Dictionary<string, object?> { ["order_id"] = 30002, ["order_amount"] = 299.5, ["currency"] = "USD", ["is_vip"] = true }, null),
            ],
            null, sinkWriters, CancellationToken.None, 1, "se:b2");

        var v3MetaJson = Encoding.UTF8.GetString(store["/lake/evolve/orders/schema/metadata/v3.metadata.json"]);
        v3MetaJson.ShouldContain("\"current-schema-id\": 1");
        v3MetaJson.ShouldContain("\"last-column-id\": 15");
        v3MetaJson.ShouldContain("\"schema-id\": 1");

        // Rename currency -> iso_currency (keeps FieldId=14!), Drop is_vip (FieldId=15 retired), Add region (MUST get FieldId=16!)
        var evolved2 = await schemaSvc.EvolveSchemaAsync(stream, sink, new EvolveCdcLakehouseSchemaInput
        {
            RenameColumns = [new RenameCdcLakehouseColumnInput { FromName = "currency", ToName = "iso_currency" }],
            DropColumns = ["is_vip"],
            AddColumns = [new AddCdcLakehouseColumnInput { Name = "region", Type = "string" }],
            DryRun = false,
        });
        evolved2.PreviousSchemaId.ShouldBe(1);
        evolved2.NewSchemaId.ShouldBe(2);
        evolved2.LastColumnId.ShouldBe(16); // NEVER reused dropped FieldId 15!
        evolved2.MetadataVersion.ShouldBe(4);
        evolved2.ActiveFields.ShouldContain(f => f.FieldId == 14 && f.Name == "iso_currency");
        evolved2.ActiveFields.ShouldContain(f => f.FieldId == 16 && f.Name == "region");
        evolved2.ActiveFields.ShouldNotContain(f => f.Name == "is_vip");

        // Commit v5 using legacy payload key "currency" -> auto-mapped to renamed "iso_currency" (FieldId 14) + "region" (FieldId 16)!
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "s:3", t3,
                    new Dictionary<string, object?> { ["order_id"] = 30003, ["order_amount"] = 500.75, ["currency"] = "EUR", ["region"] = "EMEA" }, null),
            ],
            null, sinkWriters, CancellationToken.None, 2, "se:b3");

        // Compact all 3 mixed-schema Parquet files (v1: 12 cols, v3: 15 cols w/ currency, v5: 15 cols w/ iso_currency+region)
        var compactor = new CdcObjectStorageCompactionService(factory, config, NullLogger<CdcObjectStorageCompactionService>.Instance);
        var compRes = await compactor.CompactAsync(stream, sink, minFiles: 2, deleteCompactedFiles: false, CancellationToken.None);
        compRes.Compacted.ShouldBeTrue();
        compRes.ConsolidatedRowCount.ShouldBe(3);

        // Verify compacted Parquet preserved exact FieldIds (13, 14, 16) AND historical USD value mapped under renamed iso_currency!
        var compactBytes = store[$"/lake/{compRes.OutputObjectKey}"];
        await using var mem = new MemoryStream(compactBytes, writable: false);
        await using var pqReader = await ParquetReader.CreateAsync(mem);
        var fidByName = pqReader.Metadata!.Schema
            .Where(s => s.FieldId.HasValue && !string.IsNullOrEmpty(s.Name))
            .ToDictionary(s => s.Name, s => s.FieldId!.Value, StringComparer.Ordinal);
        fidByName["order_amount"].ShouldBe(13);
        fidByName["iso_currency"].ShouldBe(14);
        fidByName["region"].ShouldBe(16);

        var previewSvc = new CdcLakehouseDataPreviewService(factory, config, historyReader);
        var preview = await previewSvc.PreviewAsync(stream, sink, requestedVersion: null, maxRows: 10);
        preview.RowCount.ShouldBe(3);
        preview.Columns.ShouldContain("order_amount");
        preview.Columns.ShouldContain("iso_currency");
        preview.Columns.ShouldContain("region");
        preview.Rows.Select(r => r["iso_currency"]).ShouldBe([null, "USD", "EUR"]);
        preview.Rows.Select(r => r["region"]).ShouldBe([null, null, "EMEA"]);

        var hist = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        hist.CurrentSchemaId.ShouldBe(2);
        hist.LastColumnId.ShouldBe(16);
        hist.SchemaFields.Count.ShouldBe(15); // 12 core + order_amount + iso_currency + region
    }

    [Fact]
    public async Task EvolvedSchema_1000Row_Throughput_And_RpoZero_SlaBenchmark()
    {
        var store = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new MemoryS3Handler(store);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.36",
            })
            .Build();

        var sink = new DataSource(
            Guid.NewGuid(),
            "minio-evolve-sla",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"http://198.51.100.36:9000","region":"us-east-1","bucket":"lake","prefix":"sla30","format":"parquet","pathStyle":true,"accessKey":"ak","secretKey":"sk"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-v300-sla",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_orders_sla30",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders/sla30",
            targetSinkKeyColumns: "order_id",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);

        var sinkWriter = new ObjectStorageEtlSinkWriter(factory, config);
        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter> { [DataSourceType.ObjectStorage] = sinkWriter };
        var schemaSvc = new IcebergV2SchemaEvolutionService(factory, config);
        var baseTs = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

        // Seed v1 then evolve schema with 4 typed promoted columns
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [new PgOutputCapturedChange("shop", "orders", "insert", "seed:0", baseTs, new Dictionary<string, object?> { ["order_id"] = 0 }, null)],
            null, sinkWriters, CancellationToken.None, 0, "sla30:seed");
        await schemaSvc.EvolveSchemaAsync(stream, sink, new EvolveCdcLakehouseSchemaInput
        {
            AddColumns =
            [
                new AddCdcLakehouseColumnInput { Name = "order_amount", Type = "double" },
                new AddCdcLakehouseColumnInput { Name = "item_count", Type = "long" },
                new AddCdcLakehouseColumnInput { Name = "currency", Type = "string" },
                new AddCdcLakehouseColumnInput { Name = "is_vip", Type = "boolean" },
            ],
            DryRun = false,
        });

        const int batchCount = 10;
        const int rowsPerBatch = 100;
        var sw = Stopwatch.StartNew();
        for (var b = 0; b < batchCount; b++)
        {
            var changes = new List<PgOutputCapturedChange>(rowsPerBatch);
            for (var i = 0; i < rowsPerBatch; i++)
            {
                var id = b * rowsPerBatch + i + 1;
                changes.Add(new PgOutputCapturedChange(
                    "shop", "orders", "insert", $"sla:{id}", baseTs.AddSeconds(id),
                    new Dictionary<string, object?>
                    {
                        ["order_id"] = id,
                        ["order_amount"] = 10.5 * (i + 1),
                        ["item_count"] = (long)(i % 5 + 1),
                        ["currency"] = i % 2 == 0 ? "USD" : "EUR",
                        ["is_vip"] = i % 10 == 0,
                    },
                    null));
            }

            await CdcExternalSinkDeliveryAdapter.DeliverAsync(
                stream, sink, changes, null, sinkWriters, CancellationToken.None, b * rowsPerBatch + 1, $"sla30:b{b}");
        }

        sw.Stop();
        var history = await new DeltaLakeHistoryReader(factory, config).ReadAsync(stream, sink, requestedVersion: null);
        history.CurrentSchemaId.ShouldBe(1);
        history.LastColumnId.ShouldBe(16);
        history.SelectedTotalRecordCount.ShouldBe(batchCount * rowsPerBatch + 1);
        var rowsPerSec = (batchCount * rowsPerBatch) / Math.Max(0.001, sw.Elapsed.TotalSeconds);
        _output.WriteLine($"[v3.0.0 Iceberg Schema-Evolved SLA] 1,000 typed rows (16 cols) in {sw.Elapsed.TotalMilliseconds:F1} ms ({rowsPerSec:F0} rows/s, RPO=0)");
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

            if (request.Method == HttpMethod.Delete)
            {
                _objects.Remove(path);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }
    }
}
