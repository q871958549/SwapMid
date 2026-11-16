using System;
using System.Collections.Generic;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class CdcProtocolHelpersTests
{
    [Theory]
    [InlineData("swapmid_pub")]
    [InlineData("_slot_01")]
    [InlineData("orders_pgoutput_slot")]
    public void ValidateReplicationIdentifier_Should_Accept_Safe_Identifiers(string identifier)
    {
        CdcProtocolHelpers.ValidateReplicationIdentifier($" {identifier} ", "SlotName")
            .ShouldBe(identifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("InvalidUpper")]
    [InlineData("123_starts_digit")]
    [InlineData("bad;drop table")]
    [InlineData("bad-dash")]
    public void ValidateReplicationIdentifier_Should_Reject_Unsafe_Or_Malformed_Identifiers(string identifier)
    {
        var ex = Should.Throw<BusinessException>(() =>
            CdcProtocolHelpers.ValidateReplicationIdentifier(identifier, "SlotName"));
        ex.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);
    }

    [Fact]
    public void ParseMonitoredTables_Should_Normalize_Dedup_And_Default_Public_Schema()
    {
        var specs = CdcProtocolHelpers.ParseMonitoredTables("orders, sales.invoices; PUBLIC.ORDERS\nfinance.payments");

        specs.Count.ShouldBe(3);
        specs[0].FullName.ShouldBe("public.orders");
        specs[1].FullName.ShouldBe("sales.invoices");
        specs[2].FullName.ShouldBe("finance.payments");
    }

    [Fact]
    public void BuildCreatePublicationSql_Should_Quote_Validated_Identifiers()
    {
        var tables = CdcProtocolHelpers.ParseMonitoredTables("public.etl_demo_source_orders,sales.events");
        var sql = CdcProtocolHelpers.BuildCreatePublicationSql("swapmid_cdc_pub", tables);

        sql.ShouldBe("CREATE PUBLICATION \"swapmid_cdc_pub\" FOR TABLE \"public\".\"etl_demo_source_orders\", \"sales\".\"events\"");
        CdcProtocolHelpers.BuildCreatePublicationSql("swapmid_all_pub", []).ShouldBe("CREATE PUBLICATION \"swapmid_all_pub\" FOR ALL TABLES");
    }

    [Fact]
    public void CompareLsn_Should_Compare_64Bit_Wal_Offsets_Numerically()
    {
        CdcProtocolHelpers.CompareLsn("0/FFFFFFFF", "1/00000000").ShouldBeLessThan(0);
        CdcProtocolHelpers.CompareLsn("0/16A2B88", "0/16A2B88").ShouldBe(0);
        CdcProtocolHelpers.CompareLsn("2/10", "1/FFFFFFFF").ShouldBeGreaterThan(0);
        CdcProtocolHelpers.CompareLsn(null, "0/100").ShouldBeLessThan(0);
    }

    [Fact]
    public void ShouldCaptureRelation_Should_Honor_Table_Filter()
    {
        var specs = CdcProtocolHelpers.ParseMonitoredTables("public.etl_demo_source_orders");
        CdcProtocolHelpers.ShouldCaptureRelation("public", "etl_demo_source_orders", specs).ShouldBeTrue();
        CdcProtocolHelpers.ShouldCaptureRelation("PUBLIC", "ETL_DEMO_SOURCE_ORDERS", specs).ShouldBeTrue();
        CdcProtocolHelpers.ShouldCaptureRelation("public", "other_table", specs).ShouldBeFalse();
        CdcProtocolHelpers.ShouldCaptureRelation("other_schema", "anything", []).ShouldBeTrue();
    }

    [Fact]
    public void DeterministicIds_And_PayloadJson_Should_Be_Repeatable()
    {
        var streamId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        var id1 = CdcProtocolHelpers.BuildDeterministicEventId(streamId, "0/1500000", 0, "insert", "public", "orders");
        var id2 = CdcProtocolHelpers.BuildDeterministicEventId(streamId, "0/1500000", 0, "insert", "public", "orders");
        var id3 = CdcProtocolHelpers.BuildDeterministicEventId(streamId, "0/1500000", 1, "insert", "public", "orders");

        id1.ShouldBe(id2);
        id1.ShouldNotBe(id3);

        var run1 = CdcProtocolHelpers.BuildDeterministicPipelineRunId(streamId, "0/1500000");
        var run2 = CdcProtocolHelpers.BuildDeterministicPipelineRunId(streamId, "0/1500000");
        run1.ShouldBe(run2);

        CdcProtocolHelpers.BuildEventSubject("Public", "Etl_Orders", "INSERT")
            .ShouldBe("cdc.public.etl_orders.insert");

        var json = CdcProtocolHelpers.BuildEventPayloadJson(
            streamId,
            "pg-orders-cdc",
            "public",
            "orders",
            "update",
            "0/1500000",
            new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, object?> { ["order_id"] = 42L, ["status"] = "paid" },
            new Dictionary<string, object?> { ["order_id"] = 42L, ["status"] = "pending" });

        json.ShouldContain("\"operation\":\"update\"");
        json.ShouldContain("\"lsn\":\"0/1500000\"");
        json.ShouldContain("\"changedColumns\":[\"status\"]");
        json.ShouldContain("\"status\":\"paid\"");
    }

    [Fact]
    public void ComputeChangedColumns_And_ExternalSinkAdapter_Should_Exclude_Toast_Unchanged_And_Map_Rows()
    {
        var changed = CdcProtocolHelpers.ComputeChangedColumns(
            new Dictionary<string, object?> { ["id"] = 1, ["status"] = "SHIPPED", ["notes"] = "__toast_unchanged__" },
            new Dictionary<string, object?> { ["id"] = 1, ["status"] = "PAID", ["notes"] = "large-text" });
        changed.ShouldBe(["status"]);

        CdcProtocolHelpers.ComputeChangedColumns(new Dictionary<string, object?> { ["id"] = 1 }, null).ShouldBeNull();
        CdcProtocolHelpers.MapReplicaIdentityCode("d").ShouldBe("default");
        CdcProtocolHelpers.MapReplicaIdentityCode("f").ShouldBe("full");
        CdcProtocolHelpers.MapReplicaIdentityCode("i").ShouldBe("index");
        CdcProtocolHelpers.MapReplicaIdentityCode("n").ShouldBe("nothing");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "pg-orders-kafka",
            Guid.NewGuid(),
            "swapmid_orders_pub",
            "swapmid_orders_slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "cdc.orders.events");

        var rows = CdcExternalSinkDeliveryAdapter.BuildRows(
            stream,
            [
                new PgOutputCapturedChange(
                    "public",
                    "orders",
                    "update",
                    "0/1600000",
                    new DateTime(2026, 11, 16, 1, 2, 3, DateTimeKind.Utc),
                    new Dictionary<string, object?> { ["order_id"] = 99L, ["status"] = "SHIPPED" },
                    new Dictionary<string, object?> { ["order_id"] = 99L, ["status"] = "PAID" }),
            ]);

        rows.Count.ShouldBe(1);
        rows[0].Length.ShouldBe(11);
        rows[0][4].ShouldBe("orders");
        rows[0][5].ShouldBe("update");
        rows[0][8].ShouldBe("[\"status\"]");
        var afterJson = rows[0][9].ShouldBeOfType<string>();
        var beforeJson = rows[0][10].ShouldBeOfType<string>();
        afterJson.ShouldContain("SHIPPED");
        beforeJson.ShouldContain("PAID");
    }

    [Fact]
    public void PartitionKeys_KafkaKeyResolution_And_ClickHouseDdl_Should_Be_Deterministic()
    {
        var cols = CdcProtocolHelpers.ParseSinkKeyColumns("order_id, tenant_id, ORDER_ID");
        cols.ShouldBe(["order_id", "tenant_id"]);

        var badKeyEx = Should.Throw<BusinessException>(() =>
            CdcProtocolHelpers.ParseSinkKeyColumns("bad-key"));
        badKeyEx.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "pg-orders-kafka",
            Guid.NewGuid(),
            "swapmid_orders_pub",
            "swapmid_orders_slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "cdc.orders.events",
            targetSinkKeyColumns: "order_id");

        var insertChange = new PgOutputCapturedChange(
            "public",
            "orders",
            "insert",
            "0/1600010",
            new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Utc),
            new Dictionary<string, object?> { ["order_id"] = 91001L, ["status"] = "CREATED" },
            null);
        var deleteChange = new PgOutputCapturedChange(
            "public",
            "orders",
            "delete",
            "0/1600020",
            new DateTime(2026, 11, 16, 1, 0, 5, DateTimeKind.Utc),
            null,
            new Dictionary<string, object?> { ["order_id"] = 91001L, ["status"] = "DRAFT" });

        var keys = CdcExternalSinkDeliveryAdapter.BuildPartitionKeys(stream, [insertChange, deleteChange]);
        keys.ShouldNotBeNull();
        keys![0].ShouldBe("public.orders:order_id=91001");
        keys[1].ShouldBe("public.orders:order_id=91001");

        var batchWithKeys = new EtlSourceBatch(
            ["event_id"],
            [typeof(string)],
            [["e1"], ["e2"]],
            batchIndex: 0,
            partitionKeys: keys);
        KafkaEtlSinkWriter.ResolveMessageKey(batchWithKeys, 0, [], Guid.Empty).ShouldBe("public.orders:order_id=91001");

        var etlBatch = new EtlSourceBatch(
            ["id", "tenant"],
            [typeof(long), typeof(string)],
            [[42L, "acme"]],
            batchIndex: 0);
        KafkaEtlSinkWriter.ResolveMessageKey(etlBatch, 0, ["tenant", "id"], Guid.Empty).ShouldBe("tenant=acme|id=42");

        var ddl = CdcExternalSinkTemplateBuilder.BuildClickHouseDdl("analytics", "cdc_orders");
        ddl.ShouldContain("CREATE TABLE IF NOT EXISTS `analytics`.`cdc_orders`");
        ddl.ShouldContain("`event_id` String");
        ddl.ShouldContain("ENGINE = MergeTree()");

        var keyedDdl = CdcExternalSinkTemplateBuilder.BuildClickHouseDdl("analytics", "cdc_orders_keyed", ["order_id", "tenant_id"]);
        keyedDdl.ShouldContain("ENGINE = ReplacingMergeTree(`commit_timestamp_utc`)");
        keyedDdl.ShouldContain("JSONExtractRaw(coalesce(`after_json`, `before_json`, '{}'), 'order_id')");
        keyedDdl.ShouldContain("JSONExtractRaw(coalesce(`after_json`, `before_json`, '{}'), 'tenant_id')");
    }

    [Fact]
    public void CdcStreamDefinition_Lifecycle_And_Counters_Should_Progress_Monotonically()
    {
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "pg-orders-cdc",
            Guid.NewGuid(),
            "swapmid_orders_pub",
            "swapmid_orders_slot",
            CdcTargetMode.MessageBusTopic,
            targetTopicName: "integration.cdc.events",
            initialSnapshotEnabled: true);

        stream.InitialSnapshotEnabled.ShouldBeTrue();
        stream.SnapshotStatus.ShouldBe(CdcSnapshotStatus.NotStarted);
        stream.MarkSnapshotSucceeded("0/1500050", 12, DateTime.UtcNow);
        stream.SnapshotStatus.ShouldBe(CdcSnapshotStatus.Completed);
        stream.SnapshotRowCount.ShouldBe(12);
        stream.TotalCapturedCount.ShouldBe(12);
        stream.CheckpointLsn.ShouldBe("0/1500050");

        stream.Status.ShouldBe(CdcStreamStatus.Draft);
        stream.Activate();
        stream.Status.ShouldBe(CdcStreamStatus.Active);

        stream.MarkCaptureSucceeded("0/1500100", 5, DateTime.UtcNow);
        stream.CheckpointLsn.ShouldBe("0/1500100");
        stream.LastCapturedCount.ShouldBe(5);
        stream.TotalCapturedCount.ShouldBe(17);

        stream.MarkCaptureFailed(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed, "TransientFailure");
        stream.Status.ShouldBe(CdcStreamStatus.Error);
        stream.TotalCapturedCount.ShouldBe(17);

        stream.MarkCaptureSucceeded("0/1500200", 3, DateTime.UtcNow);
        stream.Status.ShouldBe(CdcStreamStatus.Active);
        stream.CheckpointLsn.ShouldBe("0/1500200");
        stream.LastCapturedCount.ShouldBe(3);
        stream.TotalCapturedCount.ShouldBe(20);

        stream.Pause();
        stream.Status.ShouldBe(CdcStreamStatus.Paused);
        stream.ResetCheckpoint("0/1400000");
        stream.CheckpointLsn.ShouldBe("0/1400000");
        stream.ResetSnapshot();
        stream.SnapshotStatus.ShouldBe(CdcSnapshotStatus.NotStarted);
    }
}
