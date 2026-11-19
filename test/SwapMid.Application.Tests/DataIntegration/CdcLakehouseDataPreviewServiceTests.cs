using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Parquet;
using Parquet.Schema;
using Shouldly;
using SwapMid.Infrastructure;
using Xunit;

namespace SwapMid.DataIntegration;

public class CdcLakehouseDataPreviewServiceTests
{
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

    private static IConfiguration BuildAllowedMinioConfig()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.10",
            })
            .Build();

    private static DataSource BuildObjectStorageSink(string format)
        => new(
            Guid.NewGuid(),
            "minio-lake",
            DataSourceType.ObjectStorage,
            $$"""
              {
                "provider": "minio",
                "endpoint": "http://198.51.100.10:9000",
                "region": "us-east-1",
                "bucket": "swapmid-lake",
                "prefix": "lakehouse",
                "format": "{{format}}",
                "pathStyle": true,
                "accessKey": "ak",
                "secretKey": "sk"
              }
              """);

    [Fact]
    public async Task PreviewAsync_DeltaLake_Should_Decode_Historical_V0_Vs_Latest_V1_And_Enforce_MaxRows()
    {
        var v0Parquet = await BuildParquetBytesAsync(
            ["r", "r"],
            [1L, 2L],
            ["{\"order_no\":\"ORD-1\"}", "{\"order_no\":\"ORD-2\"}"]);
        var v1Parquet = await BuildParquetBytesAsync(
            ["c", "u", "d"],
            [3L, 1L, 2L],
            ["{\"order_no\":\"ORD-3\"}", "{\"order_no\":\"ORD-1-UPD\"}", "{}"]);

        var latestManifest = """{"latestDeltaVersion":1,"recentFiles":[]}""";
        var commit0 = """
                      {"commitInfo":{"timestamp":1763510000000,"operation":"STREAMING UPDATE","operationParameters":{"swapmidRunId":"run-0"}}}
                      {"add":{"path":"part_0.parquet","size":1024,"modificationTime":1763510000000,"dataChange":true,"stats":"{\"numRecords\":2}"}}
                      """;
        var commit1 = """
                      {"commitInfo":{"timestamp":1763510060000,"operation":"STREAMING UPDATE","operationParameters":{"swapmidRunId":"run-1"}}}
                      {"add":{"path":"part_1.parquet","size":2048,"modificationTime":1763510060000,"dataChange":true,"stats":"{\"numRecords\":3}"}}
                      """;

        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["/swapmid-lake/lakehouse/orders/_swapmid/latest.json"] = Encoding.UTF8.GetBytes(latestManifest),
            ["/swapmid-lake/lakehouse/orders/_delta_log/00000000000000000000.json"] = Encoding.UTF8.GetBytes(commit0),
            ["/swapmid-lake/lakehouse/orders/_delta_log/00000000000000000001.json"] = Encoding.UTF8.GetBytes(commit1),
            ["/swapmid-lake/lakehouse/orders/part_0.parquet"] = v0Parquet,
            ["/swapmid-lake/lakehouse/orders/part_1.parquet"] = v1Parquet,
        };

        var factory = CreateHttpClientFactory(objects);
        var cfg = BuildAllowedMinioConfig();
        var historyReader = new DeltaLakeHistoryReader(factory, cfg);
        var service = new CdcLakehouseDataPreviewService(factory, cfg, historyReader);

        var sink = BuildObjectStorageSink("parquet");
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-delta",
            Guid.NewGuid(),
            "pub",
            "slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders",
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);

        var v0Preview = await service.PreviewAsync(stream, sink, requestedVersion: 0, maxRows: 10);
        v0Preview.SelectedVersion.ShouldBe(0);
        v0Preview.RowCount.ShouldBe(2);
        v0Preview.Truncated.ShouldBeFalse();
        v0Preview.Columns.ShouldBe(["op", "id", "after_json"]);
        v0Preview.Rows[0]["op"].ShouldBe("r");
        v0Preview.Rows[1]["id"].ShouldBe("2");

        var v1Truncated = await service.PreviewAsync(stream, sink, requestedVersion: 1, maxRows: 3);
        v1Truncated.SelectedVersion.ShouldBe(1);
        v1Truncated.RowCount.ShouldBe(3);
        v1Truncated.Truncated.ShouldBeTrue();
        v1Truncated.ScannedFileCount.ShouldBe(2);
    }

    [Fact]
    public async Task PreviewAsync_Should_Handle_Vacuumed_404_Historical_Fragment_Gracefully()
    {
        var latestManifest = """{"latestDeltaVersion":0,"recentFiles":[]}""";
        var commit0 = """
                      {"commitInfo":{"timestamp":1763510000000,"operation":"STREAMING UPDATE"}}
                      {"add":{"path":"deleted_old.parquet","size":1024,"modificationTime":1763510000000,"dataChange":true,"stats":"{\"numRecords\":2}"}}
                      """;

        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["/swapmid-lake/lakehouse/orders/_swapmid/latest.json"] = Encoding.UTF8.GetBytes(latestManifest),
            ["/swapmid-lake/lakehouse/orders/_delta_log/00000000000000000000.json"] = Encoding.UTF8.GetBytes(commit0),
        };

        var factory = CreateHttpClientFactory(objects);
        var cfg = BuildAllowedMinioConfig();
        var service = new CdcLakehouseDataPreviewService(factory, cfg, new DeltaLakeHistoryReader(factory, cfg));
        var sink = BuildObjectStorageSink("parquet");
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-delta",
            Guid.NewGuid(),
            "pub",
            "slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders",
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);

        var preview = await service.PreviewAsync(stream, sink, requestedVersion: 0, maxRows: 10);
        preview.RowCount.ShouldBe(0);
        preview.MissingFileCount.ShouldBe(1);
        preview.Summary.ShouldContain("物理清理");
    }

    [Fact]
    public async Task PreviewAsync_Jsonl_Should_Decode_SwapMidManifest_Latest_Rows()
    {
        var latestJson = """
                         {
                           "recentFiles": [
                             { "dataObjectKey": "lakehouse/orders/part_0.jsonl", "rowCount": 2 }
                           ]
                         }
                         """;
        var jsonl = "{\"op\":\"c\",\"order_no\":\"ORD-99\",\"amount\":128.5}\n{\"op\":\"u\",\"order_no\":\"ORD-99\",\"amount\":130.0}\n";
        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["/swapmid-lake/lakehouse/orders/_swapmid/latest.json"] = Encoding.UTF8.GetBytes(latestJson),
            ["/swapmid-lake/lakehouse/orders/part_0.jsonl"] = Encoding.UTF8.GetBytes(jsonl),
        };

        var factory = CreateHttpClientFactory(objects);
        var cfg = BuildAllowedMinioConfig();
        var service = new CdcLakehouseDataPreviewService(factory, cfg, new DeltaLakeHistoryReader(factory, cfg));
        var sink = BuildObjectStorageSink("jsonl");
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-jsonl",
            Guid.NewGuid(),
            "pub",
            "slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders",
            lakehouseTableFormat: LakehouseTableFormat.SwapMidManifest);

        var preview = await service.PreviewAsync(stream, sink, requestedVersion: null, maxRows: 10);
        preview.FileFormat.ShouldBe("jsonl");
        preview.RowCount.ShouldBe(2);
        preview.Columns.ShouldContain("order_no");
        preview.Rows[0]["order_no"].ShouldBe("ORD-99");
        preview.Rows[1]["amount"].ShouldBe("130.0");
    }

    private static IHttpClientFactory CreateHttpClientFactory(Dictionary<string, byte[]> objects)
    {
        var handler = new InMemoryObjectHandler(objects);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    private sealed class InMemoryObjectHandler(Dictionary<string, byte[]> objects) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (objects.TryGetValue(path, out var data))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(data),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
