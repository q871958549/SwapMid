using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Monitoring;
using Prometheus;
using Shouldly;
using Xunit;

namespace SwapMid.MonitoringTests;

public sealed class EtlExecutionMetricInitializationTests
{
    private static readonly string[] ExpectedAdmissionLabels =
    [
        "acquired",
        "busy",
        "idempotent_replay",
        "coordination_failure",
        "capacity_exhausted",
    ];

    private static readonly string[] ExpectedCoordinationLabels =
    [
        "database_fence_failure",
        "database_fence_conflict",
        "target_commit_outcome_unknown",
        "ownership_lost",
        "database_fence_release_failure",
        "lock_release_failure",
        "running_durable_state_blocked",
        "state_commit_outcome_unknown",
        "projection_write_failure",
        "target_quarantine_blocked",
        "legacy_durable_state_blocked",
        "durable_state_scan_limit",
        "terminal_store_topology_mismatch",
        "terminal_store_topology_probe_failure",
        "running_recovery_succeeded",
        "running_recovery_late_finalize_blocked",
        "target_receipt_absent",
        "target_receipt_mismatch",
        "target_receipt_unavailable",
        "target_receipt_verified",
        "target_receipt_manual_override_blocked",
    ];

    private static readonly string[] ExpectedResultLabels =
    [
        "succeeded",
        "failed",
        "cancelled",
        "outcome_unknown",
        "unexpected",
    ];

    [Fact]
    public async Task 固定标签必须在首个ETL事件前以零值完整发布()
    {
        var registry = Metrics.NewCustomRegistry();
        var metricFactory = Metrics.WithCustomRegistry(registry);
        var admissionCounter = metricFactory.CreateCounter(
            "test_etl_execution_admission_total",
            "测试 ETL 准入固定标签预初始化",
            new CounterConfiguration { LabelNames = ["outcome"] });
        var coordinationCounter = metricFactory.CreateCounter(
            "test_etl_execution_coordination_events_total",
            "测试 ETL 协调事件固定标签预初始化",
            new CounterConfiguration { LabelNames = ["outcome"] });
        var resultCounter = metricFactory.CreateCounter(
            "test_etl_execution_results_total",
            "测试 ETL 结果固定标签预初始化",
            new CounterConfiguration { LabelNames = ["result"] });

        PlatformMetrics.PublishEtlExecutionSeries(
            admissionCounter,
            coordinationCounter,
            resultCounter);

        await using var stream = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(
            stream,
            CancellationToken.None);
        var exposition = Encoding.UTF8.GetString(stream.ToArray());

        AssertZeroSamples(
            exposition,
            "test_etl_execution_admission_total",
            "outcome",
            ExpectedAdmissionLabels);
        AssertZeroSamples(
            exposition,
            "test_etl_execution_coordination_events_total",
            "outcome",
            ExpectedCoordinationLabels);
        AssertZeroSamples(
            exposition,
            "test_etl_execution_results_total",
            "result",
            ExpectedResultLabels);
    }

    private static void AssertZeroSamples(
        string exposition,
        string metricName,
        string labelName,
        string[] expectedLabels)
    {
        var samples = exposition
            .Split('\n')
            .Where(line => line.StartsWith(
                $"{metricName}{{",
                System.StringComparison.Ordinal))
            .ToList();

        samples.Count.ShouldBe(expectedLabels.Length);
        foreach (var label in expectedLabels)
        {
            samples.ShouldContain(
                $"{metricName}{{{labelName}=\"{label}\"}} 0");
        }
    }
}
