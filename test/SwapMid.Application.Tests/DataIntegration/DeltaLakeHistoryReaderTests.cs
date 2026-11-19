using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class DeltaLakeHistoryReaderTests
{
    [Fact]
    public async Task ReadAsync_Should_Reconstruct_TimeTravel_Snapshots_Across_Append_And_Optimize()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-delta",
            Guid.NewGuid(),
            "mysql_binlog",
            "binlog_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "orders/delta",
            sourceType: CdcSourceType.MySqlBinlog,
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);
        var sink = CreateMinioSink(stream.TargetSinkId!.Value);

        var run0 = Guid.NewGuid();
        var run1 = Guid.NewGuid();
        var run2 = Guid.NewGuid();
        var compactRun = Guid.NewGuid();

        store["/swapmid-lake/cdc/orders/delta/_delta_log/00000000000000000000.json"] =
            DeltaLakeCommitWriter.BuildAppendNdjson(
                0, stream, run0, $"part_{run0:N}.parquet", 1024, 1730000000000L, "{\"numRecords\":10}", isInitialSnapshot: true);
        store["/swapmid-lake/cdc/orders/delta/_delta_log/00000000000000000001.json"] =
            DeltaLakeCommitWriter.BuildAppendNdjson(
                1, stream, run1, $"part_{run1:N}.parquet", 512, 1730000010000L, "{\"numRecords\":4}", isInitialSnapshot: false);
        store["/swapmid-lake/cdc/orders/delta/_delta_log/00000000000000000002.json"] =
            DeltaLakeCommitWriter.BuildAppendNdjson(
                2, stream, run2, $"part_{run2:N}.parquet", 768, 1730000020000L, "{\"numRecords\":6}", isInitialSnapshot: false);
        store["/swapmid-lake/cdc/orders/delta/_delta_log/00000000000000000003.json"] =
            DeltaLakeCommitWriter.BuildOptimizeNdjson(
                3, stream, compactRun, [$"part_{run0:N}.parquet", $"part_{run1:N}.parquet"], $"compact_{compactRun:N}.parquet", 1800, 1730000030000L, "{\"numRecords\":14}");

        store["/swapmid-lake/cdc/orders/delta/_swapmid/latest.json"] =
            "{\"latestDeltaVersion\":3,\"destination\":\"orders/delta\"}";

        var reader = CreateReader(store);

        // Latest version view (post-OPTIMIZE compaction: active files are compact_3 + part_2)
        var latest = await reader.ReadAsync(stream, sink, requestedVersion: null, maxCommits: 32);
        latest.CurrentVersion.ShouldBe(3);
        latest.SelectedVersion.ShouldBe(3);
        latest.TotalCommitCount.ShouldBe(4);
        latest.Commits.Count.ShouldBe(4);
        latest.Commits[0].Version.ShouldBe(3);
        latest.Commits[0].Operation.ShouldBe("OPTIMIZE");
        latest.Commits[0].RemovedFileCount.ShouldBe(2);
        latest.SelectedActiveFileCount.ShouldBe(2);
        latest.SelectedTotalRecordCount.ShouldBe(20);
        latest.SelectedTotalBytes.ShouldBe(2568);
        latest.ActiveFiles.ShouldContain(f => f.RelativePath == $"compact_{compactRun:N}.parquet");
        latest.ActiveFiles.ShouldContain(f => f.RelativePath == $"part_{run2:N}.parquet");
        latest.ActiveFiles.ShouldNotContain(f => f.RelativePath == $"part_{run0:N}.parquet");
        latest.TimeTravelQuerySql.ShouldNotBeNull();
        latest.TimeTravelQuerySql.ShouldContain("VERSION AS OF 3");

        // Time-Travel to v1 (pre-compaction historical snapshot: part_0 + part_1, before part_2 existed)
        var historical = await reader.ReadAsync(stream, sink, requestedVersion: 1, maxCommits: 32);
        historical.CurrentVersion.ShouldBe(3);
        historical.SelectedVersion.ShouldBe(1);
        historical.SelectedActiveFileCount.ShouldBe(2);
        historical.SelectedTotalRecordCount.ShouldBe(14);
        historical.SelectedTotalBytes.ShouldBe(1536);
        historical.ActiveFiles.ShouldContain(f => f.RelativePath == $"part_{run0:N}.parquet");
        historical.ActiveFiles.ShouldContain(f => f.RelativePath == $"part_{run1:N}.parquet");
        historical.ActiveFiles.ShouldNotContain(f => f.RelativePath == $"part_{run2:N}.parquet");
        historical.TimeTravelQuerySql.ShouldNotBeNull();
        historical.TimeTravelQuerySql.ShouldContain("VERSION AS OF 1");
        historical.TimeTravelQuerySql.ShouldContain("version=1");
    }

    [Fact]
    public async Task ReadAsync_Should_Reject_Out_Of_Range_Version_And_Handle_SwapMidManifest_Gracefully()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-delta",
            Guid.NewGuid(),
            "mysql_binlog",
            "binlog_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "orders/delta",
            sourceType: CdcSourceType.MySqlBinlog,
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);
        var sink = CreateMinioSink(stream.TargetSinkId!.Value);

        var run0 = Guid.NewGuid();
        store["/swapmid-lake/cdc/orders/delta/_delta_log/00000000000000000000.json"] =
            DeltaLakeCommitWriter.BuildAppendNdjson(
                0, stream, run0, $"part_{run0:N}.parquet", 512, 1730000000000L, "{\"numRecords\":5}", isInitialSnapshot: true);
        store["/swapmid-lake/cdc/orders/delta/_swapmid/latest.json"] = "{\"latestDeltaVersion\":0}";

        var reader = CreateReader(store);
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            reader.ReadAsync(stream, sink, requestedVersion: 99, maxCommits: 10));
        ex.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);

        var manifestStream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-manifest-only",
            Guid.NewGuid(),
            "swapmid_pub",
            "swapmid_slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders/manifest",
            lakehouseTableFormat: LakehouseTableFormat.SwapMidManifest);
        var emptyHistory = await reader.ReadAsync(manifestStream, sink, requestedVersion: null, maxCommits: 10);
        emptyHistory.TableFormat.ShouldBe(LakehouseTableFormat.SwapMidManifest);
        emptyHistory.TotalCommitCount.ShouldBe(0);
    }

    private static DeltaLakeHistoryReader CreateReader(Dictionary<string, string> store)
    {
        var handler = new ReadOnlyS3Handler(store);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.10",
            })
            .Build();
        return new DeltaLakeHistoryReader(factory, config);
    }

    private static DataSource CreateMinioSink(Guid id) => new(
        id,
        "minio-delta",
        DataSourceType.ObjectStorage,
        """{"provider":"minio","endpoint":"http://198.51.100.10:9000","region":"us-east-1","bucket":"swapmid-lake","prefix":"cdc","format":"parquet","pathStyle":true,"accessKey":"AK","secretKey":"SK"}""",
        description: null);

    private sealed class ReadOnlyS3Handler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _store;

        public ReadOnlyS3Handler(Dictionary<string, string> store) => _store = store;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                _store.TryGetValue(request.RequestUri!.AbsolutePath, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
