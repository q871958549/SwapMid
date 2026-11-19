using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace SwapMid.DataIntegration;

/// <summary>
/// 自动化 Chaos SLA 吞吐与 RPO=0 基准测试套件：
/// 对 1,000 条 CDC 变更事件（10 批 x 100 行）端到端压测真实 Parquet 编码、Delta Lake / Apache Iceberg v2 CAS 提交、
/// 故障重放幂等收敛、Time-Travel 回放及 Data Contract SLA 评估，并输出可复现的吞吐与 P50/P95/P99 延迟指标。
/// </summary>
public sealed class ChaosSlaThroughputBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public ChaosSlaThroughputBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Benchmark_1000_Events_Parquet_Delta_And_IcebergV2_RpoZero_And_Sla_Latency()
    {
        const int batchCount = 10;
        const int rowsPerBatch = 100;
        const int expectedTotalRows = batchCount * rowsPerBatch;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.55",
            })
            .Build();

        var deltaMetrics = await RunLakehouseBenchmarkAsync(
            LakehouseTableFormat.DeltaLake,
            batchCount,
            rowsPerBatch,
            config);

        var icebergMetrics = await RunLakehouseBenchmarkAsync(
            LakehouseTableFormat.ApacheIceberg,
            batchCount,
            rowsPerBatch,
            config);

        deltaMetrics.TotalRecordsInLatestSnapshot.ShouldBe(expectedTotalRows);
        deltaMetrics.CrashReplayConvergedIdempotently.ShouldBeTrue();
        deltaMetrics.ContractStatusAfterReplay.ShouldBe(CdcContractStatus.Healthy);
        deltaMetrics.P99BatchMs.ShouldBeLessThan(5000);

        icebergMetrics.TotalRecordsInLatestSnapshot.ShouldBe(expectedTotalRows);
        icebergMetrics.CrashReplayConvergedIdempotently.ShouldBeTrue();
        icebergMetrics.ContractStatusAfterReplay.ShouldBe(CdcContractStatus.Healthy);
        icebergMetrics.P99BatchMs.ShouldBeLessThan(5000);

        _output.WriteLine(
            $"[Benchmark DeltaLake] rows={deltaMetrics.TotalRecordsInLatestSnapshot} batches={batchCount} " +
            $"elapsedMs={deltaMetrics.TotalElapsedMs:F1} rowsPerSec={deltaMetrics.RowsPerSec:F0} " +
            $"p50Ms={deltaMetrics.P50BatchMs:F2} p95Ms={deltaMetrics.P95BatchMs:F2} p99Ms={deltaMetrics.P99BatchMs:F2} " +
            $"timeTravelMs={deltaMetrics.TimeTravelReplayMs:F2} previewRows={deltaMetrics.PreviewRowCount} RPO=0");

        _output.WriteLine(
            $"[Benchmark IcebergV2] rows={icebergMetrics.TotalRecordsInLatestSnapshot} batches={batchCount} " +
            $"elapsedMs={icebergMetrics.TotalElapsedMs:F1} rowsPerSec={icebergMetrics.RowsPerSec:F0} " +
            $"p50Ms={icebergMetrics.P50BatchMs:F2} p95Ms={icebergMetrics.P95BatchMs:F2} p99Ms={icebergMetrics.P99BatchMs:F2} " +
            $"timeTravelMs={icebergMetrics.TimeTravelReplayMs:F2} previewRows={icebergMetrics.PreviewRowCount} RPO=0");
    }

    private static async Task<LakehouseBenchmarkReport> RunLakehouseBenchmarkAsync(
        LakehouseTableFormat format,
        int batchCount,
        int rowsPerBatch,
        IConfiguration config)
    {
        var store = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new InMemoryBenchmarkS3Handler(store);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var sink = new DataSource(
            Guid.NewGuid(),
            $"bench-{format}",
            DataSourceType.ObjectStorage,
            """
            {
              "provider": "minio",
              "endpoint": "http://198.51.100.55:9000",
              "region": "us-east-1",
              "bucket": "bench-lake",
              "prefix": "sla",
              "format": "parquet",
              "pathStyle": true,
              "accessKey": "ak",
              "secretKey": "sk"
            }
            """);

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            $"bench-stream-{format}",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_bench",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: $"orders/{format.ToString().ToLowerInvariant()}",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: format);
        stream.Activate();
        stream.ConfigureDataContract(maxFreshnessLagSeconds: 60, maxAllowedWalLagBytes: null, requiredColumns: null);

        var writer = new ObjectStorageEtlSinkWriter(factory, config);
        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter>
        {
            [DataSourceType.ObjectStorage] = writer,
        };

        var batchLatencies = new List<double>(batchCount);
        var totalWatch = Stopwatch.StartNew();
        IReadOnlyList<PgOutputCapturedChange>? replayBatch5 = null;
        string? replayKey5 = null;

        for (var b = 0; b < batchCount; b++)
        {
            var changes = new List<PgOutputCapturedChange>(rowsPerBatch);
            var ts = DateTime.UtcNow;
            for (var r = 0; r < rowsPerBatch; r++)
            {
                var seq = (b * rowsPerBatch) + r + 1;
                changes.Add(new PgOutputCapturedChange(
                    "shop",
                    "orders",
                    r % 5 == 0 ? "update" : "insert",
                    $"webhook:{seq}",
                    ts,
                    new Dictionary<string, object?>
                    {
                        ["order_id"] = seq,
                        ["amount"] = 100.0 + (r * 1.5),
                        ["status"] = "PAID",
                    },
                    null));
            }

            var idempotencyKey = $"webhook:bench-delivery-{b}";
            if (b == 4)
            {
                replayBatch5 = changes;
                replayKey5 = idempotencyKey;
            }

            var sw = Stopwatch.StartNew();
            var written = await CdcExternalSinkDeliveryAdapter.DeliverAsync(
                stream, sink, changes, batchEndLsn: null, sinkWriters, CancellationToken.None,
                eventSequenceOffset: b * rowsPerBatch, idempotencyKeyOverride: idempotencyKey);
            sw.Stop();

            written.ShouldBe(rowsPerBatch);
            stream.MarkCaptureSucceeded($"webhook:{(b + 1) * rowsPerBatch}", rowsPerBatch, DateTime.UtcNow);
            batchLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        totalWatch.Stop();

        // Simulate mid-flight crash replay of batch #5 with identical delivery idempotency key
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink, replayBatch5!, batchEndLsn: null, sinkWriters, CancellationToken.None,
            eventSequenceOffset: 4 * rowsPerBatch, idempotencyKeyOverride: replayKey5);

        var historyReader = new DeltaLakeHistoryReader(factory, config);
        var ttWatch = Stopwatch.StartNew();
        var latestHistory = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        var midVersion = format == LakehouseTableFormat.ApacheIceberg ? 5L : 4L;
        var midHistory = await historyReader.ReadAsync(stream, sink, requestedVersion: midVersion);
        ttWatch.Stop();

        midHistory.SelectedTotalRecordCount.ShouldBe(5 * rowsPerBatch);

        var previewService = new CdcLakehouseDataPreviewService(factory, config, historyReader);
        var preview = await previewService.PreviewAsync(stream, sink, requestedVersion: latestHistory.CurrentVersion, maxRows: 25);

        var contractEval = CdcDataContractEvaluator.EvaluateSnapshot(stream, DateTime.UtcNow);

        var sorted = batchLatencies.OrderBy(x => x).ToArray();
        return new LakehouseBenchmarkReport(
            TotalRecordsInLatestSnapshot: latestHistory.SelectedTotalRecordCount,
            CrashReplayConvergedIdempotently: latestHistory.TotalCommitCount == batchCount,
            TotalElapsedMs: totalWatch.Elapsed.TotalMilliseconds,
            RowsPerSec: (batchCount * rowsPerBatch) / Math.Max(0.001, totalWatch.Elapsed.TotalSeconds),
            P50BatchMs: Percentile(sorted, 0.50),
            P95BatchMs: Percentile(sorted, 0.95),
            P99BatchMs: Percentile(sorted, 0.99),
            TimeTravelReplayMs: ttWatch.Elapsed.TotalMilliseconds,
            PreviewRowCount: preview.RowCount,
            ContractStatusAfterReplay: contractEval.Status);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var idx = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }

    private sealed record LakehouseBenchmarkReport(
        long TotalRecordsInLatestSnapshot,
        bool CrashReplayConvergedIdempotently,
        double TotalElapsedMs,
        double RowsPerSec,
        double P50BatchMs,
        double P95BatchMs,
        double P99BatchMs,
        double TimeTravelReplayMs,
        int PreviewRowCount,
        CdcContractStatus ContractStatusAfterReplay);

    private sealed class InMemoryBenchmarkS3Handler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _objects;

        public InMemoryBenchmarkS3Handler(Dictionary<string, byte[]> objects)
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
