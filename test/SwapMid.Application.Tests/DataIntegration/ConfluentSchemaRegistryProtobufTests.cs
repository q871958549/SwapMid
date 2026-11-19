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
using Xunit;

namespace SwapMid.DataIntegration;

public class ConfluentSchemaRegistryProtobufTests
{
    private const string DebeziumProtoSchema = """
    syntax = "proto3";
    package dbserver1.shop;

    message Envelope {
      message OrderRow {
        int64 order_id = 1;
        string order_no = 2;
        double amount = 3;
        bool priority = 4;
      }
      message Source {
        string schema = 1;
        string table = 2;
      }
      OrderRow before = 1;
      OrderRow after = 2;
      Source source = 3;
      string op = 4;
      int64 ts_ms = 5;
    }
    """;

    [Fact]
    public async Task KafkaDebeziumCdcCaptureService_Should_Decode_Confluent_Protobuf_Wire_Format_And_Cache_Schema()
    {
        ConfluentSchemaRegistryClient.ClearCacheForTesting();
        var fetches = 0;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/schemas/ids/77")
            {
                Interlocked.Increment(ref fetches);
                var json = JsonSerializer.Serialize(new
                {
                    schemaType = "PROTOBUF",
                    schema = DebeziumProtoSchema,
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.33",
            })
            .Build();

        var kafkaSource = new DataSource(
            Guid.NewGuid(),
            "redpanda-sr-proto",
            DataSourceType.Kafka,
            """{"provider":"kafka","bootstrapServers":"198.51.100.33:9092","securityProtocol":"plaintext","schemaRegistryUrl":"http://198.51.100.33:8081"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-proto-cdc",
            kafkaSource.Id,
            "dbserver1.shop.orders_proto",
            "cg_orders_proto",
            CdcTargetMode.MessageBusTopic,
            targetTopicName: "integration.cdc.events",
            monitoredTables: "shop.orders",
            sourceType: CdcSourceType.KafkaDebezium);

        var ts1 = DateTimeOffset.Parse("2026-11-19T12:00:00Z").ToUnixTimeMilliseconds();
        var frame1 = BuildConfluentProtobufEnvelope(77, before: null, after: (27001, "ORD-PROTO-001", 499.75, true), "shop", "orders", "c", ts1);
        var frame2 = BuildConfluentProtobufEnvelope(77, before: (27001, "ORD-PROTO-001", 499.75, true), after: (27001, "ORD-PROTO-001", 520.25, false), "shop", "orders", "u", ts1 + 5000);

        var session = new FakeKafkaSession(
            [new KafkaPartitionWatermark(0, 0, 2)],
            [
                new KafkaConsumedRecord(0, 0, null, DateTime.UtcNow, frame1),
                new KafkaConsumedRecord(0, 1, null, DateTime.UtcNow, frame2),
            ]);

        var publisher = Substitute.For<IIntegrationEventPublisher>();
        var events = new List<IntegrationEventEnvelope>();
        publisher.PublishIfTopicActiveAsync(Arg.Do<IntegrationEventEnvelope>(e => events.Add(e)), Arg.Any<CancellationToken>())
            .Returns(true);

        var service = new KafkaDebeziumCdcCaptureService(
            config,
            publisher,
            null!,
            Substitute.For<IEtlRelationalConnectionFactory>(),
            NullLogger<KafkaDebeziumCdcCaptureService>.Instance,
            sinkWriters: null,
            sessionFactory: (_, _) => session,
            httpClientFactory: factory);

        var result = await service.CaptureBatchAsync(kafkaSource, stream, targetSink: null, CancellationToken.None);
        result.CapturedEventCount.ShouldBe(2);
        result.NewCheckpointLsn.ShouldBe("kafka:0=2");
        fetches.ShouldBe(1);
        events.Count.ShouldBe(2);
        events[0].PayloadJson.ShouldContain("ORD-PROTO-001");
        events[0].PayloadJson.ShouldContain("499.75");
        events[1].PayloadJson.ShouldContain("520.25");
    }

    [Fact]
    public void ProtobufWireDatumReader_Should_Decode_Nested_Message_Index_Enum_Map_And_Packed_Repeated()
    {
        const string proto = """
        syntax = "proto3";
        package demo;
        enum State { UNKNOWN = 0; ACTIVE = 2; }
        message Outer {
          message Inner {
            sint32 delta = 1;
            State state = 2;
            repeated int32 tags = 3;
            map<string, string> labels = 4;
          }
        }
        """;

        using var innerMs = new MemoryStream();
        // field 1 (sint32 delta = -7 -> zigzag = 13)
        WriteVarInt(innerMs, (1UL << 3) | 0);
        WriteVarInt(innerMs, EncodeZigZag(-7));
        // field 2 (enum State = ACTIVE = 2)
        WriteVarInt(innerMs, (2UL << 3) | 0);
        WriteVarInt(innerMs, 2);
        // field 3 (packed repeated int32 [10, 20])
        using (var packed = new MemoryStream())
        {
            WriteVarInt(packed, 10);
            WriteVarInt(packed, 20);
            WriteLengthDelimited(innerMs, 3, packed.ToArray());
        }

        // field 4 (map entry env -> prod)
        using (var entry = new MemoryStream())
        {
            WriteStringField(entry, 1, "env");
            WriteStringField(entry, 2, "prod");
            WriteLengthDelimited(innerMs, 4, entry.ToArray());
        }

        // Confluent message index array [0, 0] -> count=2 (zz 4), idx0=0 (zz 0), idx1=0 (zz 0)
        using var frame = new MemoryStream();
        WriteVarInt(frame, EncodeZigZag(2));
        WriteVarInt(frame, EncodeZigZag(0));
        WriteVarInt(frame, EncodeZigZag(0));
        innerMs.Position = 0;
        innerMs.CopyTo(frame);

        var decoded = ProtobufWireDatumReader.DecodeConfluentPayload(proto, frame.ToArray())!.AsObject();
        decoded["delta"]!.GetValue<int>().ShouldBe(-7);
        decoded["state"]!.GetValue<string>().ShouldBe("ACTIVE");
        decoded["tags"]!.AsArray().Count.ShouldBe(2);
        decoded["labels"]!["env"]!.GetValue<string>().ShouldBe("prod");
    }

    internal static byte[] BuildConfluentProtobufEnvelope(
        int schemaId,
        (long Id, string No, double Amount, bool Priority)? before,
        (long Id, string No, double Amount, bool Priority)? after,
        string schema,
        string table,
        string op,
        long tsMs)
    {
        using var body = new MemoryStream();
        if (before.HasValue) WriteLengthDelimited(body, 1, EncodeOrderRow(before.Value));
        if (after.HasValue) WriteLengthDelimited(body, 2, EncodeOrderRow(after.Value));
        using (var src = new MemoryStream())
        {
            WriteStringField(src, 1, schema);
            WriteStringField(src, 2, table);
            WriteLengthDelimited(body, 3, src.ToArray());
        }

        WriteStringField(body, 4, op);
        WriteVarInt(body, (5UL << 3) | 0);
        WriteVarInt(body, unchecked((ulong)tsMs));

        using var full = new MemoryStream();
        full.WriteByte(0x00);
        Span<byte> idBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(idBuf, schemaId);
        full.Write(idBuf);
        full.WriteByte(0x00); // single-byte Confluent message index shortcut [0]
        body.Position = 0;
        body.CopyTo(full);
        return full.ToArray();
    }

    private static byte[] EncodeOrderRow((long Id, string No, double Amount, bool Priority) row)
    {
        using var ms = new MemoryStream();
        WriteVarInt(ms, (1UL << 3) | 0);
        WriteVarInt(ms, unchecked((ulong)row.Id));
        WriteStringField(ms, 2, row.No);
        WriteVarInt(ms, (3UL << 3) | 1);
        Span<byte> dbl = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(dbl, BitConverter.DoubleToInt64Bits(row.Amount));
        ms.Write(dbl);
        if (row.Priority)
        {
            WriteVarInt(ms, (4UL << 3) | 0);
            WriteVarInt(ms, 1);
        }

        return ms.ToArray();
    }

    private static void WriteStringField(Stream ms, int fieldNum, string val)
        => WriteLengthDelimited(ms, fieldNum, Encoding.UTF8.GetBytes(val));

    private static void WriteLengthDelimited(Stream ms, int fieldNum, byte[] payload)
    {
        WriteVarInt(ms, ((ulong)fieldNum << 3) | 2);
        WriteVarInt(ms, (ulong)payload.Length);
        ms.Write(payload);
    }

    private static void WriteVarInt(Stream ms, ulong value)
    {
        while (value >= 0x80)
        {
            ms.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        ms.WriteByte((byte)value);
    }

    private static ulong EncodeZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) => Task.FromResult(_fn(req));
    }

    private sealed class FakeKafkaSession : IKafkaCdcBrokerSession
    {
        private readonly IReadOnlyList<KafkaPartitionWatermark> _watermarks;
        private readonly IReadOnlyList<KafkaConsumedRecord> _records;
        public FakeKafkaSession(IReadOnlyList<KafkaPartitionWatermark> wm, IReadOnlyList<KafkaConsumedRecord> rec)
        {
            _watermarks = wm;
            _records = rec;
        }

        public IReadOnlyList<KafkaPartitionWatermark> ProbeTopicWatermarks(string topic, TimeSpan timeout) => _watermarks;
        public IReadOnlyList<KafkaConsumedRecord> PollBounded(string topic, IReadOnlyDictionary<int, long> s, IReadOnlyDictionary<int, long> h, int m, TimeSpan d, CancellationToken ct) => _records;
        public void Dispose() { }
    }
}
