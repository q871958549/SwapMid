using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

public class DeltaLakeCommitWriterTests
{
    [Fact]
    public void BuildAppendNdjson_Should_Emit_Valid_Protocol_MetaData_On_V0_And_Append_Only_On_V1()
    {
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "mysql-delta-orders",
            Guid.NewGuid(),
            "mysql_binlog",
            "binlog_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "cdc/orders",
            sourceType: CdcSourceType.MySqlBinlog,
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);

        var run0 = Guid.NewGuid();
        var v0 = DeltaLakeCommitWriter.BuildAppendNdjson(
            version: 0,
            stream: stream,
            runId: run0,
            relativeDataPath: $"run_{run0:N}.parquet",
            byteSize: 2048,
            timestampMs: 1730000000000L,
            statsJson: "{\"numRecords\":12}",
            isInitialSnapshot: true);

        var lines0 = v0.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines0.Length.ShouldBe(4);
        using (var d0 = JsonDocument.Parse(lines0[0]))
        {
            d0.RootElement.GetProperty("commitInfo").GetProperty("operation").GetString().ShouldBe("STREAMING UPDATE");
            d0.RootElement.GetProperty("commitInfo").TryGetProperty("readVersion", out _).ShouldBeFalse();
        }
        using (var d1 = JsonDocument.Parse(lines0[1]))
        {
            d1.RootElement.GetProperty("protocol").GetProperty("minReaderVersion").GetInt32().ShouldBe(1);
            d1.RootElement.GetProperty("protocol").GetProperty("minWriterVersion").GetInt32().ShouldBe(2);
        }
        using (var d2 = JsonDocument.Parse(lines0[2]))
        {
            d2.RootElement.GetProperty("metaData").GetProperty("format").GetProperty("provider").GetString().ShouldBe("parquet");
            (d2.RootElement.GetProperty("metaData").GetProperty("schemaString").GetString() ?? string.Empty)
                .ShouldContain("\"commit_timestamp_utc\",\"type\":\"timestamp\"");
        }
        using (var d3 = JsonDocument.Parse(lines0[3]))
        {
            d3.RootElement.GetProperty("add").GetProperty("path").GetString().ShouldBe($"run_{run0:N}.parquet");
            d3.RootElement.GetProperty("add").GetProperty("dataChange").GetBoolean().ShouldBeTrue();
        }

        var run1 = Guid.NewGuid();
        var v1 = DeltaLakeCommitWriter.BuildAppendNdjson(
            version: 1,
            stream: stream,
            runId: run1,
            relativeDataPath: $"run_{run1:N}.parquet",
            byteSize: 1024,
            timestampMs: 1730000010000L,
            statsJson: "{\"numRecords\":3}",
            isInitialSnapshot: false);

        var lines1 = v1.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines1.Length.ShouldBe(2);
        using var c1 = JsonDocument.Parse(lines1[0]);
        c1.RootElement.GetProperty("commitInfo").GetProperty("readVersion").GetInt64().ShouldBe(0);
    }

    [Fact]
    public void BuildOptimizeNdjson_Should_Emit_Remove_And_Add_With_DataChange_False()
    {
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "delta-opt",
            Guid.NewGuid(),
            "swapmid_pub",
            "swapmid_slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "cdc/orders",
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);

        var compactRun = Guid.NewGuid();
        var payload = DeltaLakeCommitWriter.BuildOptimizeNdjson(
            version: 2,
            stream: stream,
            compactRunId: compactRun,
            removedRelativePaths: ["run_aaa.parquet", "run_bbb.parquet"],
            addedRelativePath: $"compact_{compactRun:N}.parquet",
            outputByteSize: 4096,
            timestampMs: 1730000020000L,
            statsJson: "{\"numRecords\":15}");

        var lines = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(4);
        JsonDocument.Parse(lines[0]).RootElement.GetProperty("commitInfo").GetProperty("operation").GetString().ShouldBe("OPTIMIZE");
        JsonDocument.Parse(lines[1]).RootElement.GetProperty("remove").GetProperty("dataChange").GetBoolean().ShouldBeFalse();
        JsonDocument.Parse(lines[2]).RootElement.GetProperty("remove").GetProperty("path").GetString().ShouldBe("run_bbb.parquet");
        JsonDocument.Parse(lines[3]).RootElement.GetProperty("add").GetProperty("dataChange").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task CommitAppendAsync_Should_Advance_On_412_Conflict_And_Be_Idempotent_On_Replay()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "delta-cas",
            Guid.NewGuid(),
            "swapmid_pub",
            "swapmid_slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "cdc/orders",
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);

        var run0 = Guid.NewGuid();
        store["/lake/cdc/orders/_delta_log/00000000000000000000.json"] =
            DeltaLakeCommitWriter.BuildAppendNdjson(
                0, stream, run0, "run_0.parquet", 100, 1730000000000L, "{\"numRecords\":1}", false);

        var handler = new InMemoryS3Handler(store);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.10",
            })
            .Build();
        var s3Cfg = new ObjectStorageSinkConfig(
            "minio", "http://198.51.100.10:9000", "us-east-1", "lake",
            "", "parquet", true, "ak", "sk", "");
        var latestDoc = new CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument
        {
            StreamId = stream.Id,
            StreamName = stream.Name,
            Destination = "cdc/orders",
            LatestDeltaVersion = null,
        };

        var replayVer = await DeltaLakeCommitWriter.CommitAppendAsync(
            factory, config, s3Cfg, "cdc/orders", stream, latestDoc,
            run0, "cdc/orders/run_0.parquet", 100, 1, false, DateTime.UtcNow, CancellationToken.None);
        replayVer.ShouldBe(0);

        // Simulate concurrent writer claiming v1 after pre-check GET returns 404 (forces 412 -> advance to v2).
        handler.SimulateConcurrentWriteOnFirstPut("/lake/cdc/orders/_delta_log/00000000000000000001.json",
            DeltaLakeCommitWriter.BuildAppendNdjson(
                1, stream, Guid.NewGuid(), "run_other.parquet", 50, 1730000005000L, "{\"numRecords\":1}", false));
        latestDoc.LatestDeltaVersion = 0;
        var run2 = Guid.NewGuid();
        var v2 = await DeltaLakeCommitWriter.CommitAppendAsync(
            factory, config, s3Cfg, "cdc/orders", stream, latestDoc,
            run2, "cdc/orders/run_2.parquet", 250, 5, false, DateTime.UtcNow, CancellationToken.None);
        v2.ShouldBe(2);
        store.ContainsKey("/lake/cdc/orders/_delta_log/00000000000000000002.json").ShouldBeTrue();
        handler.IfNoneMatchHeadersSeen.ShouldAllBe(h => h == "*");
    }

    private sealed class InMemoryS3Handler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _objects;
        private readonly Dictionary<string, string> _concurrentRaceInjections = new(StringComparer.Ordinal);
        public List<string> IfNoneMatchHeadersSeen { get; } = [];

        public InMemoryS3Handler(Dictionary<string, string> objects) => _objects = objects;

        public void SimulateConcurrentWriteOnFirstPut(string path, string competingPayload)
            => _concurrentRaceInjections[path] = competingPayload;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                if (_objects.TryGetValue(path, out var body))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Put)
            {
                if (_concurrentRaceInjections.Remove(path, out var racer))
                {
                    _objects[path] = racer;
                }

                if (request.Headers.TryGetValues("If-None-Match", out var vals))
                {
                    IfNoneMatchHeadersSeen.AddRange(vals);
                    if (_objects.ContainsKey(path))
                    {
                        return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                    }
                }

                _objects[path] = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }
    }
}
