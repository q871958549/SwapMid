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
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class IcebergV2MaintenanceTests
{
    [Fact]
    public async Task CreateTag_And_ExpireSnapshots_Should_Protect_Tagged_Snapshot_And_Reclaim_Unreferenced_Orphans()
    {
        var store = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new MemoryS3Handler(store);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.32",
            })
            .Build();

        var sink = new DataSource(
            Guid.NewGuid(),
            "minio-maint",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"http://198.51.100.32:9000","region":"us-east-1","bucket":"lake","prefix":"maint","format":"parquet","pathStyle":true,"accessKey":"ak","secretKey":"sk"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-iceberg-maint",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_orders_maint",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders/wap",
            targetSinkKeyColumns: "order_id",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);
        stream.ConfigureLakehouseWriteMode(LakehouseWriteMode.MergeOnReadUpsert);

        var sinkWriter = new ObjectStorageEtlSinkWriter(factory, config);
        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter> { [DataSourceType.ObjectStorage] = sinkWriter };
        var t1 = new DateTime(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

        // v1: initial insert (27001, 27002)
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "w:1", t1,
                    new Dictionary<string, object?> { ["order_id"] = 27001, ["amount"] = 100m }, null),
                new PgOutputCapturedChange("shop", "orders", "insert", "w:2", t1,
                    new Dictionary<string, object?> { ["order_id"] = 27002, ["amount"] = 200m }, null),
            ],
            null, sinkWriters, CancellationToken.None, 0, "w:b1");

        // v2: MoR update 27001 + delete 27002 (emits eqdel_*.parquet + *-d0.avro)
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "update", "w:3", t2,
                    new Dictionary<string, object?> { ["order_id"] = 27001, ["amount"] = 150m },
                    new Dictionary<string, object?> { ["order_id"] = 27001, ["amount"] = 100m }),
                new PgOutputCapturedChange("shop", "orders", "delete", "w:4", t2,
                    null,
                    new Dictionary<string, object?> { ["order_id"] = 27002, ["amount"] = 200m }),
            ],
            null, sinkWriters, CancellationToken.None, 2, "w:b2");
        var v2EqDelKeys = store.Keys.Where(k => k.Contains("/eqdel_", StringComparison.Ordinal) && k.EndsWith(".parquet", StringComparison.Ordinal)).ToList();
        v2EqDelKeys.Count.ShouldBe(1);

        // v3: REPLACE compaction -> clean single data file, 0 eqdel
        var compactor = new CdcObjectStorageCompactionService(factory, config, NullLogger<CdcObjectStorageCompactionService>.Instance);
        (await compactor.CompactAsync(stream, sink, minFiles: 2, deleteCompactedFiles: false, CancellationToken.None)).Compacted.ShouldBeTrue();

        // Pin audit tag on v1 (creates metadata v4)
        var maint = new IcebergV2MaintenanceService(factory, config);
        var tagRes = await maint.CreateTagAsync(stream, sink, new CreateCdcLakehouseTagInput
        {
            TagName = "audit-v1",
            SnapshotVersion = 1,
            MaxRefAgeMs = 86_400_000,
        });
        tagRes.TagName.ShouldBe("audit-v1");
        tagRes.SnapshotVersion.ShouldBe(1);
        tagRes.MetadataVersion.ShouldBe(4);

        // Reject reserved tag name "main"
        await Should.ThrowAsync<BusinessException>(async () =>
            await maint.CreateTagAsync(stream, sink, new CreateCdcLakehouseTagInput { TagName = "main" }));

        // Subsequent CDC commit (v5) MUST preserve "audit-v1" tag in refs!
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "w:5", t3,
                    new Dictionary<string, object?> { ["order_id"] = 27003, ["amount"] = 300m }, null),
            ],
            null, sinkWriters, CancellationToken.None, 4, "w:b3");

        var historyReader = new DeltaLakeHistoryReader(factory, config);
        var histBeforeExpire = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        histBeforeExpire.Refs.ShouldContain(r => r.Name == "main" && r.Type == "branch" && r.Version == 5);
        histBeforeExpire.Refs.ShouldContain(r => r.Name == "audit-v1" && r.Type == "tag" && r.Version == 1);
        histBeforeExpire.TotalCommitCount.ShouldBe(4); // v1, v2, v3, v5

        // DryRun expireSnapshots(RetainLast = 1): v5 retained (current/retainLast), v1 PROTECTED by tag audit-v1, v2 & v3 expired!
        var dryPlan = await maint.ExpireSnapshotsAsync(stream, sink, new ExpireCdcLakehouseSnapshotsInput
        {
            RetainLast = 1,
            OlderThanMinutes = 0,
            DeleteOrphanedFiles = true,
            DryRun = true,
        });
        dryPlan.DryRun.ShouldBeTrue();
        dryPlan.RetainedSnapshotCount.ShouldBe(2); // v5 + v1 (tag)
        dryPlan.PinnedByRefCount.ShouldBe(1);
        dryPlan.ExpiredSnapshotCount.ShouldBe(2); // v2, v3
        dryPlan.ExpiredVersions.ShouldBe([2L, 3L]);
        dryPlan.CandidateOrphanFileCount.ShouldBeGreaterThan(0);
        dryPlan.DeletedOrphanFileCount.ShouldBe(0);

        // Execute expireSnapshots with orphan file deletion
        var execRes = await maint.ExpireSnapshotsAsync(stream, sink, new ExpireCdcLakehouseSnapshotsInput
        {
            RetainLast = 1,
            OlderThanMinutes = 0,
            DeleteOrphanedFiles = true,
            DryRun = false,
        });
        execRes.DryRun.ShouldBeFalse();
        execRes.NewMetadataVersion.ShouldBe(6);
        execRes.RetainedSnapshotCount.ShouldBe(2);
        execRes.ExpiredSnapshotCount.ShouldBe(2);
        execRes.DeletedOrphanFileCount.ShouldBe(execRes.CandidateOrphanFileCount);

        // Superseded v2 equality delete file should have been physically deleted, while v1 and v5 remain readable!
        foreach (var oldKey in v2EqDelKeys)
        {
            store.ContainsKey(oldKey).ShouldBeFalse();
        }

        var previewSvc = new CdcLakehouseDataPreviewService(factory, config, historyReader);
        var v1Preview = await previewSvc.PreviewAsync(stream, sink, requestedVersion: 1, maxRows: 20);
        v1Preview.RowCount.ShouldBe(2); // audit-v1 still readable!
        var v5Preview = await previewSvc.PreviewAsync(stream, sink, requestedVersion: 5, maxRows: 20);
        v5Preview.RowCount.ShouldBe(2); // 27001 (150) + 27003 (300)
    }

    [Fact]
    public async Task WapStagingBranch_Should_Isolate_Main_EnforceContractGate_And_FastForwardPromote_WithAuditTag()
    {
        var store = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var handler = new MemoryS3Handler(store);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.33",
            })
            .Build();

        var sink = new DataSource(
            Guid.NewGuid(),
            "minio-wap",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"http://198.51.100.33:9000","region":"us-east-1","bucket":"lake","prefix":"wap","format":"parquet","pathStyle":true,"accessKey":"ak","secretKey":"sk"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-iceberg-wap",
            Guid.NewGuid(),
            "push_webhook",
            "webhook_orders_wap",
            CdcTargetMode.ExternalSink,
            targetSinkId: sink.Id,
            targetSinkDestination: "orders/branch",
            targetSinkKeyColumns: "order_id",
            sourceType: CdcSourceType.PushWebhook,
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);
        stream.ConfigureLakehouseWriteMode(LakehouseWriteMode.MergeOnReadUpsert);

        var sinkWriter = new ObjectStorageEtlSinkWriter(factory, config);
        var sinkWriters = new Dictionary<DataSourceType, IEtlSinkWriter> { [DataSourceType.ObjectStorage] = sinkWriter };
        var t1 = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 19, 11, 0, 0, DateTimeKind.Utc);

        // v1: initial committed baseline on main
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "insert", "w:1", t1,
                    new Dictionary<string, object?> { ["order_id"] = 28001, ["amount"] = 100m }, null),
            ],
            null, sinkWriters, CancellationToken.None, 0, "wap:b1");

        // Switch stream to WAP branch "staging" + scheduled maintenance cron
        stream.ConfigureLakehouseWapAndMaintenance("staging", "0 2 * * *", 5, 1440, true);
        stream.LakehouseWriteBranch.ShouldBe("staging");
        stream.LakehouseMaintenanceCron.ShouldBe("0 2 * * *");
        stream.LakehouseExpireRetainLast.ShouldBe(5);
        stream.LakehouseDeleteOrphanedFiles.ShouldBeTrue();

        // v2: written strictly to staging branch (main MUST remain pinned to v1!)
        await CdcExternalSinkDeliveryAdapter.DeliverAsync(
            stream, sink,
            [
                new PgOutputCapturedChange("shop", "orders", "update", "w:2", t2,
                    new Dictionary<string, object?> { ["order_id"] = 28001, ["amount"] = 250m },
                    new Dictionary<string, object?> { ["order_id"] = 28001, ["amount"] = 100m }),
                new PgOutputCapturedChange("shop", "orders", "insert", "w:3", t2,
                    new Dictionary<string, object?> { ["order_id"] = 28002, ["amount"] = 400m }, null),
            ],
            null, sinkWriters, CancellationToken.None, 1, "wap:b2");

        var historyReader = new DeltaLakeHistoryReader(factory, config);
        var histBeforePromote = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        histBeforePromote.Refs.ShouldContain(r => r.Name == "main" && r.Type == "branch" && r.Version == 1);
        histBeforePromote.Refs.ShouldContain(r => r.Name == "staging" && r.Type == "branch" && r.Version == 2);

        // Contract gate blocks promotion when SLA is violated
        stream.ConfigureDataContract(maxFreshnessLagSeconds: 60, maxAllowedWalLagBytes: null, requiredColumns: "order_id");
        stream.RecordContractEvaluation(violated: true, summary: "Freshness SLA breached", evaluatedAtUtc: t2);

        var maint = new IcebergV2MaintenanceService(factory, config);
        await Should.ThrowAsync<BusinessException>(async () =>
            await maint.PromoteBranchAsync(stream, sink, new PromoteCdcLakehouseBranchInput
            {
                BranchName = "staging",
                RequireContractPass = true,
            }));

        // Clear contract violation -> DryRun passes without mutating metadata
        stream.RecordContractEvaluation(violated: false, summary: null, evaluatedAtUtc: t2);
        var dryPromote = await maint.PromoteBranchAsync(stream, sink, new PromoteCdcLakehouseBranchInput
        {
            BranchName = "staging",
            RequireContractPass = true,
            CreateAuditTagName = "release-280",
            DryRun = true,
        });
        dryPromote.DryRun.ShouldBeTrue();
        dryPromote.AlreadyUpToDate.ShouldBeFalse();
        dryPromote.PreviousMainVersion.ShouldBe(1);
        dryPromote.PromotedSnapshotVersion.ShouldBe(2);
        dryPromote.MetadataVersion.ShouldBe(2);

        // Execute fast-forward promotion with atomic audit tag
        var promoted = await maint.PromoteBranchAsync(stream, sink, new PromoteCdcLakehouseBranchInput
        {
            BranchName = "staging",
            RequireContractPass = true,
            CreateAuditTagName = "release-280",
            DryRun = false,
        });
        promoted.DryRun.ShouldBeFalse();
        promoted.AlreadyUpToDate.ShouldBeFalse();
        promoted.PreviousMainVersion.ShouldBe(1);
        promoted.PromotedSnapshotVersion.ShouldBe(2);
        promoted.MetadataVersion.ShouldBe(3);
        promoted.AuditTagName.ShouldBe("release-280");

        var histAfterPromote = await historyReader.ReadAsync(stream, sink, requestedVersion: null);
        histAfterPromote.Refs.ShouldContain(r => r.Name == "main" && r.Type == "branch" && r.Version == 2);
        histAfterPromote.Refs.ShouldContain(r => r.Name == "staging" && r.Type == "branch" && r.Version == 2);
        histAfterPromote.Refs.ShouldContain(r => r.Name == "release-280" && r.Type == "tag" && r.Version == 2);

        // Second promote is idempotent no-op
        var noop = await maint.PromoteBranchAsync(stream, sink, new PromoteCdcLakehouseBranchInput
        {
            BranchName = "staging",
            DryRun = false,
        });
        noop.AlreadyUpToDate.ShouldBeTrue();
        noop.MetadataVersion.ShouldBe(3);
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
