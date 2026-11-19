using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SwapMid.Infrastructure;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class ConfluentSchemaRegistryAvroTests
{
    private const string DebeziumAvroSchemaJson = """
    {
      "type": "record",
      "name": "Envelope",
      "namespace": "dbserver1.shop.orders",
      "fields": [
        {
          "name": "before",
          "type": ["null", {
            "type": "record",
            "name": "Value",
            "fields": [
              { "name": "order_id", "type": "int" },
              { "name": "order_no", "type": "string" },
              { "name": "amount", "type": "double" }
            ]
          }],
          "default": null
        },
        { "name": "after", "type": ["null", "Value"], "default": null },
        {
          "name": "source",
          "type": {
            "type": "record",
            "name": "Source",
            "fields": [
              { "name": "schema", "type": "string" },
              { "name": "table", "type": "string" },
              { "name": "ts_ms", "type": "long" }
            ]
          }
        },
        { "name": "op", "type": "string" },
        { "name": "ts_ms", "type": "long" }
      ]
    }
    """;

    [Fact]
    public async Task KafkaDebeziumCdcCaptureService_Should_Decode_Confluent_Avro_Wire_Payloads_And_Cache_Schema()
    {
        ConfluentSchemaRegistryClient.ClearCacheForTesting();
        var schemaFetches = 0;
        var handler = new StubSchemaRegistryHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/subjects")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""["dbserver1.shop.orders-value"]""", Encoding.UTF8, "application/json"),
                };
            }

            if (req.RequestUri.AbsolutePath == "/schemas/ids/42")
            {
                Interlocked.Increment(ref schemaFetches);
                var payload = JsonSerializer.Serialize(new { schema = DebeziumAvroSchemaJson });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.31",
            })
            .Build();

        var kafkaSource = new DataSource(
            Guid.NewGuid(),
            "redpanda-sr-avro",
            DataSourceType.Kafka,
            """{"provider":"kafka","bootstrapServers":"198.51.100.31:9092","securityProtocol":"plaintext","schemaRegistryUrl":"http://198.51.100.31:8081"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-avro-cdc",
            kafkaSource.Id,
            "dbserver1.shop.orders",
            "cg_orders_avro",
            CdcTargetMode.MessageBusTopic,
            targetTopicName: "integration.cdc.events",
            monitoredTables: "shop.orders",
            sourceType: CdcSourceType.KafkaDebezium);

        var ts1 = DateTimeOffset.Parse("2026-11-19T10:00:00Z").ToUnixTimeMilliseconds();
        var ts2 = DateTimeOffset.Parse("2026-11-19T10:05:00Z").ToUnixTimeMilliseconds();
        var raw1 = BuildConfluentDebeziumEnvelope(42, null, (26001, "ORD-AVRO-001", 199.5), "shop", "orders", "c", ts1);
        var raw2 = BuildConfluentDebeziumEnvelope(42, (26001, "ORD-AVRO-001", 199.5), (26001, "ORD-AVRO-001", 215.25), "shop", "orders", "u", ts2);

        var session = new FakeKafkaSession(
            [new KafkaPartitionWatermark(0, 0, 2)],
            [
                new KafkaConsumedRecord(0, 0, null, DateTime.UtcNow, raw1),
                new KafkaConsumedRecord(0, 1, null, DateTime.UtcNow, raw2),
            ]);

        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var capturedEvents = new List<IntegrationEventEnvelope>();
        publisher.PublishIfTopicActiveAsync(Arg.Do<IntegrationEventEnvelope>(r => capturedEvents.Add(r)), Arg.Any<CancellationToken>())
            .Returns(true);

        var service = new KafkaDebeziumCdcCaptureService(
            config,
            publisher,
            null!,
            Substitute.For<IEtlRelationalConnectionFactory>(),
            NullLogger<KafkaDebeziumCdcCaptureService>.Instance,
            sinkWriters: null,
            sessionFactory: (_, _) => session,
            httpClientFactory: httpFactory);

        var preflight = await service.PreflightAsync(kafkaSource, stream, CancellationToken.None);
        preflight.IsReady.ShouldBeTrue();
        preflight.SlotPlugin.ShouldBe("debezium-avro+json");
        preflight.Diagnostics.ShouldContain(d => d.Contains("Schema Registry", StringComparison.Ordinal));

        var result = await service.CaptureBatchAsync(kafkaSource, stream, targetSink: null, CancellationToken.None);
        result.CapturedEventCount.ShouldBe(2);
        result.NewCheckpointLsn.ShouldBe("kafka:0=2");
        schemaFetches.ShouldBe(1); // 2nd record served from Schema Registry cache
        capturedEvents.Count.ShouldBe(2);
        capturedEvents[0].PayloadJson.ShouldContain("ORD-AVRO-001");
        capturedEvents[1].PayloadJson.ShouldContain("215.25");
    }

    [Fact]
    public async Task ConfluentSchemaRegistryClient_Should_Block_Ssrf_Metadata_Endpoint()
    {
        ConfluentSchemaRegistryClient.ClearCacheForTesting();
        var config = new ConfigurationBuilder().Build();
        var kafkaCfg = KafkaSinkConfigContract.ParseForWrite(
            """{"provider":"kafka","bootstrapServers":"redpanda:9092","schemaRegistryUrl":"http://169.254.169.254/latest"}""");
        var factory = Substitute.For<IHttpClientFactory>();

        await Should.ThrowAsync<BusinessException>(async () =>
            await ConfluentSchemaRegistryClient.FetchAvroSchemaAsync(factory, config, kafkaCfg, 1, CancellationToken.None));
    }

    internal static byte[] BuildConfluentDebeziumEnvelope(
        int schemaId,
        (int Id, string No, double Amount)? before,
        (int Id, string No, double Amount)? after,
        string schema,
        string table,
        string op,
        long tsMs)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x00);
        Span<byte> idBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(idBuf, schemaId);
        ms.Write(idBuf);

        WriteOptionalOrder(ms, before);
        WriteOptionalOrder(ms, after);
        IcebergAvroOcfCodec.WriteString(ms, schema);
        IcebergAvroOcfCodec.WriteString(ms, table);
        IcebergAvroOcfCodec.WriteLong(ms, tsMs);
        IcebergAvroOcfCodec.WriteString(ms, op);
        IcebergAvroOcfCodec.WriteLong(ms, tsMs);
        return ms.ToArray();
    }

    private static void WriteOptionalOrder(Stream ms, (int Id, string No, double Amount)? order)
    {
        if (!order.HasValue)
        {
            IcebergAvroOcfCodec.WriteInt(ms, 0);
            return;
        }

        IcebergAvroOcfCodec.WriteInt(ms, 1);
        IcebergAvroOcfCodec.WriteInt(ms, order.Value.Id);
        IcebergAvroOcfCodec.WriteString(ms, order.Value.No);
        Span<byte> dbl = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(dbl, BitConverter.DoubleToInt64Bits(order.Value.Amount));
        ms.Write(dbl);
    }

    private sealed class FakeKafkaSession : IKafkaCdcBrokerSession
    {
        private readonly IReadOnlyList<KafkaPartitionWatermark> _watermarks;
        private readonly IReadOnlyList<KafkaConsumedRecord> _records;
        public FakeKafkaSession(IReadOnlyList<KafkaPartitionWatermark> watermarks, IReadOnlyList<KafkaConsumedRecord> records)
        {
            _watermarks = watermarks;
            _records = records;
        }

        public IReadOnlyList<KafkaPartitionWatermark> ProbeTopicWatermarks(string topic, TimeSpan timeout) => _watermarks;
        public IReadOnlyList<KafkaConsumedRecord> PollBounded(string topic, IReadOnlyDictionary<int, long> s, IReadOnlyDictionary<int, long> h, int max, TimeSpan d, CancellationToken ct) => _records;
        public void Dispose() { }
    }

    private sealed class StubSchemaRegistryHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubSchemaRegistryHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
