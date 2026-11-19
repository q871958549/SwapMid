using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class KafkaDebeziumCdcTests
{
    [Fact]
    public void EnvelopeParser_Should_Parse_Debezium_CloudEvents_FlatJson_Tombstones_And_Checkpoints()
    {
        var fallback = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

        // 1. Standard Debezium wrapped envelope (with schema + payload)
        var wrapped = """
            {
              "schema": { "type": "struct" },
              "payload": {
                "before": { "order_id": 1001, "amount": 50.0 },
                "after": { "order_id": 1001, "amount": 75.5 },
                "source": { "db": "shopdb", "schema": "public", "table": "orders", "ts_ms": 1795046400000 },
                "op": "u",
                "ts_ms": 1795046401000
              }
            }
            """;
        KafkaDebeziumEnvelopeParser.TryParseMessage(wrapped, "dbserver1.public.orders", 0, 42, fallback, out var c1).ShouldBeTrue();
        c1.ShouldNotBeNull();
        c1.Schema.ShouldBe("public");
        c1.Table.ShouldBe("orders");
        c1.Operation.ShouldBe("update");
        c1.Lsn.ShouldBe("kafka:0:42");
        c1.Data!["order_id"].ShouldBe(1001L);
        c1.OldData!["amount"].ShouldBe(50.0m);

        // 2. Unwrapped Debezium delete event (MySQL db without schema)
        var deleteMsg = """
            {
              "before": { "order_id": 1002 },
              "after": null,
              "source": { "db": "orders_db", "table": "orders" },
              "op": "d"
            }
            """;
        KafkaDebeziumEnvelopeParser.TryParseMessage(deleteMsg, "orders_topic", 1, 7, fallback, out var c2).ShouldBeTrue();
        c2!.Schema.ShouldBe("orders_db");
        c2.Table.ShouldBe("orders");
        c2.Operation.ShouldBe("delete");
        c2.OldData!["order_id"].ShouldBe(1002L);

        // 3. Flat JSON fallback
        var flat = """{"order_id": 1003, "status": "PAID"}""";
        KafkaDebeziumEnvelopeParser.TryParseMessage(flat, "raw_orders", 2, 9, fallback, out var c3).ShouldBeTrue();
        c3!.Schema.ShouldBe("kafka");
        c3.Table.ShouldBe("raw_orders");
        c3.Operation.ShouldBe("insert");
        c3.Data!["status"].ShouldBe("PAID");

        // 4. Tombstone returns false cleanly
        KafkaDebeziumEnvelopeParser.TryParseMessage(null, "raw_orders", 2, 10, fallback, out _).ShouldBeFalse();
        KafkaDebeziumEnvelopeParser.TryParseMessage("   ", "raw_orders", 2, 11, fallback, out _).ShouldBeFalse();

        // 5. Multi-partition checkpoint round-trip & validation
        var normalized = KafkaDebeziumEnvelopeParser.NormalizeCheckpointToken("1=25, 0=10");
        normalized.ShouldBe("kafka:0=10,1=25");
        var parsed = KafkaDebeziumEnvelopeParser.ParseCheckpoints(normalized);
        parsed[0].ShouldBe(10);
        parsed[1].ShouldBe(25);
        Should.Throw<BusinessException>(() => KafkaDebeziumEnvelopeParser.NormalizeCheckpointToken("kafka:0=-1"));
        Should.Throw<BusinessException>(() => KafkaDebeziumEnvelopeParser.NormalizeCheckpointToken("invalid"));
    }

    [Fact]
    public async Task KafkaDebeziumCdcCaptureService_Should_Preflight_And_Stream_Into_DailyPartitioned_IcebergV2()
    {
        var objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new MemoryS3Handler(objects);
        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.30,198.51.100.31",
            })
            .Build();

        var kafkaSource = new DataSource(
            Guid.NewGuid(),
            "redpanda-cdc-source",
            DataSourceType.Kafka,
            """{"bootstrapServers":"198.51.100.30:9092","clientId":"swapmid-test"}""");

        var minioSink = new DataSource(
            Guid.NewGuid(),
            "minio-iceberg-daily",
            DataSourceType.ObjectStorage,
            """
            {
              "provider": "minio",
              "endpoint": "http://198.51.100.31:9000",
              "region": "us-east-1",
              "bucket": "swapmid-lake",
              "prefix": "kafka-iceberg",
              "format": "parquet",
              "pathStyle": true,
              "accessKey": "ak",
              "secretKey": "sk"
            }
            """);

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-kafka-iceberg-daily",
            kafkaSource.Id,
            "dbserver1.shop.orders",
            "swapmid_kafka_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: minioSink.Id,
            targetSinkDestination: "orders/daily",
            monitoredTables: "shop.orders",
            sourceType: CdcSourceType.KafkaDebezium,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);
        stream.ConfigureLakehousePartitionStrategy(LakehousePartitionStrategy.DailyCommitUtc);
        stream.Activate();

        var day1Ms = new DateTimeOffset(2026, 11, 18, 14, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var day2Ms = new DateTimeOffset(2026, 11, 19, 9, 15, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var fakeSession = new FakeKafkaBrokerSession(
            [new KafkaPartitionWatermark(0, 0, 2), new KafkaPartitionWatermark(1, 0, 2)],
            [
                new KafkaConsumedRecord(0, 0, $$$"""{"payload":{"op":"c","source":{"schema":"shop","table":"orders"},"after":{"order_id":1,"amount":100.5},"ts_ms":{{{day1Ms}}}}}""", DateTime.UtcNow),
                new KafkaConsumedRecord(0, 1, null, DateTime.UtcNow), // tombstone advances offset cleanly
                new KafkaConsumedRecord(1, 0, $$$"""{"payload":{"op":"u","source":{"schema":"shop","table":"orders"},"after":{"order_id":1,"amount":150.0},"ts_ms":{{{day2Ms}}}}}""", DateTime.UtcNow),
                new KafkaConsumedRecord(1, 1, $$$"""{"payload":{"op":"c","source":{"schema":"shop","table":"ignored"},"after":{"id":9},"ts_ms":{{{day2Ms}}}}}""", DateTime.UtcNow),
            ]);

        var objectWriter = new ObjectStorageEtlSinkWriter(httpFactory, config);
        var service = new KafkaDebeziumCdcCaptureService(
            config,
            Substitute.For<IIntegrationEventPublisher>(),
            executionCoordinator: null!,
            Substitute.For<IEtlRelationalConnectionFactory>(),
            NullLogger<KafkaDebeziumCdcCaptureService>.Instance,
            sinkWriters: [objectWriter],
            sessionFactory: (_, _) => fakeSession);

        var preflight = await service.PreflightAsync(kafkaSource, stream, CancellationToken.None);
        preflight.IsReady.ShouldBeTrue();
        preflight.WalLevel.ShouldBe("kafka_debezium");
        preflight.WalLagBytes.ShouldBe(4);
        preflight.CurrentWalLsn.ShouldBe("kafka:0=2,1=2");

        CdcSchemaDriftEvaluation? capturedSchemaEval = null;
        var batch = await service.CaptureBatchAsync(
            kafkaSource,
            stream,
            minioSink,
            CancellationToken.None,
            eval =>
            {
                capturedSchemaEval = eval;
                return Task.CompletedTask;
            });

        batch.CapturedEventCount.ShouldBe(2);
        batch.DeliveredCount.ShouldBe(2);
        batch.NewCheckpointLsn.ShouldBe("kafka:0=2,1=2");
        capturedSchemaEval.ShouldNotBeNull();
        capturedSchemaEval.CurrentJson.ShouldContain("order_id");
        capturedSchemaEval.CurrentJson.ShouldContain("amount");

        // Verify 2 daily-partitioned Parquet files under data/commit_day=2026-11-18 and data/commit_day=2026-11-19
        var parquetKeys = objects.Keys.Where(k => k.EndsWith(".parquet", StringComparison.Ordinal)).OrderBy(k => k).ToList();
        parquetKeys.Count.ShouldBe(2);
        parquetKeys[0].ShouldContain("/data/commit_day=2026-11-18/");
        parquetKeys[1].ShouldContain("/data/commit_day=2026-11-19/");

        // Verify Iceberg v2 metadata has spec-id=1 (day(commit_timestamp_utc)) and binary Avro bounds
        var metaBytes = objects["/swapmid-lake/kafka-iceberg/orders/daily/metadata/v1.metadata.json"];
        using var metaDoc = JsonDocument.Parse(metaBytes);
        metaDoc.RootElement.GetProperty("default-spec-id").GetInt32().ShouldBe(1);
        metaDoc.RootElement.GetProperty("last-partition-id").GetInt32().ShouldBe(1000);
        var spec1 = metaDoc.RootElement.GetProperty("partition-specs")[1];
        spec1.GetProperty("fields")[0].GetProperty("transform").GetString().ShouldBe("day");
        spec1.GetProperty("fields")[0].GetProperty("source-id").GetInt32().ShouldBe(8);
        spec1.GetProperty("fields")[0].GetProperty("field-id").GetInt32().ShouldBe(1000);

        var mlUri = metaDoc.RootElement.GetProperty("snapshots")[0].GetProperty("manifest-list").GetString()!;
        var mlKey = "/swapmid-lake/" + IcebergAvroManifestBuilder.StripS3BucketPrefix(mlUri, "swapmid-lake");
        var listEntries = IcebergAvroManifestBuilder.ReadManifestListAvro(objects[mlKey]);
        listEntries.Count.ShouldBe(1);
        listEntries[0].PartitionSpecId.ShouldBe(1);
        listEntries[0].MinPartitionDayEpoch.ShouldBe(IcebergAvroManifestBuilder.ToUtcEpochDay(new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc)));
        listEntries[0].MaxPartitionDayEpoch.ShouldBe(IcebergAvroManifestBuilder.ToUtcEpochDay(new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc)));

        var mfKey = "/swapmid-lake/" + IcebergAvroManifestBuilder.StripS3BucketPrefix(listEntries[0].ManifestPath, "swapmid-lake");
        var fileEntries = IcebergAvroManifestBuilder.ReadManifestFileAvro(objects[mfKey]);
        fileEntries.Count.ShouldBe(2);
        fileEntries.Select(e => e.PartitionDayEpoch).OrderBy(x => x).ShouldBe(
        [
            IcebergAvroManifestBuilder.ToUtcEpochDay(new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc)),
            IcebergAvroManifestBuilder.ToUtcEpochDay(new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc)),
        ]);
    }

    private sealed class FakeKafkaBrokerSession : IKafkaCdcBrokerSession
    {
        private readonly IReadOnlyList<KafkaPartitionWatermark> _watermarks;
        private readonly IReadOnlyList<KafkaConsumedRecord> _records;

        public FakeKafkaBrokerSession(
            IReadOnlyList<KafkaPartitionWatermark> watermarks,
            IReadOnlyList<KafkaConsumedRecord> records)
        {
            _watermarks = watermarks;
            _records = records;
        }

        public IReadOnlyList<KafkaPartitionWatermark> ProbeTopicWatermarks(string topic, TimeSpan timeout) => _watermarks;

        public IReadOnlyList<KafkaConsumedRecord> PollBounded(
            string topic,
            IReadOnlyDictionary<int, long> partitionStartOffsets,
            IReadOnlyDictionary<int, long> partitionHighWatermarks,
            int maxRecords,
            TimeSpan maxDuration,
            CancellationToken cancellationToken)
            => _records
                .Where(r => r.Offset >= partitionStartOffsets.GetValueOrDefault(r.Partition, 0))
                .Take(maxRecords)
                .ToList();

        public void Dispose()
        {
        }
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
