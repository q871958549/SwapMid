using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class CdcRelationalMirrorDeliveryAdapterTests
{
    [Fact]
    public void CoalesceChangesByKey_Should_Fold_Multiple_Ops_Resolve_Toast_And_Respect_DeletePolicy()
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<PgOutputCapturedChange> changes =
        [
            new("public", "orders", "snapshot", "0/10", now, new Dictionary<string, object?> { ["order_id"] = 1L, ["note"] = "hello-toast", ["amount"] = 10.5m }, null),
            new("public", "orders", "insert", "0/11", now, new Dictionary<string, object?> { ["order_id"] = 2L, ["note"] = "n2", ["amount"] = 20m }, null),
            new("public", "orders", "update", "0/12", now, new Dictionary<string, object?> { ["order_id"] = 1L, ["note"] = "__toast_unchanged__", ["amount"] = 15.25m }, null),
            new("public", "orders", "insert", "0/13", now, new Dictionary<string, object?> { ["order_id"] = 3L, ["note"] = "temp", ["amount"] = 1m }, null),
            new("public", "orders", "delete", "0/14", now, null, new Dictionary<string, object?> { ["order_id"] = 3L }),
            new("public", "orders", "delete", "0/15", now, null, new Dictionary<string, object?> { ["order_id"] = 4L }),
        ];

        var deleteEnabled = CdcRelationalMirrorRowProjector.CoalesceChangesByKey(
            changes,
            ["order_id"],
            CdcDeletePolicy.DeleteRow);

        deleteEnabled.Count.ShouldBe(4);
        deleteEnabled[0].IsDelete.ShouldBeFalse();
        deleteEnabled[0].Data!["note"].ShouldBe("hello-toast");
        deleteEnabled[0].Data!["amount"].ShouldBe(15.25m);
        deleteEnabled[1].IsDelete.ShouldBeFalse();
        deleteEnabled[2].IsDelete.ShouldBeTrue();
        deleteEnabled[2].KeyValues[0].ShouldBe(3L);
        deleteEnabled[3].IsDelete.ShouldBeTrue();
        deleteEnabled[3].KeyValues[0].ShouldBe(4L);

        var deleteIgnored = CdcRelationalMirrorRowProjector.CoalesceChangesByKey(
            changes,
            ["order_id"],
            CdcDeletePolicy.Ignore);

        deleteIgnored.Count.ShouldBe(3);
        deleteIgnored.ShouldAllBe(x => !x.IsDelete);
    }

    [Fact]
    public void CoalesceChangesByKey_Should_Support_FieldMappings_And_SoftDelete_IntraBatch_Payload()
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<CdcFieldMappingRule> mappings =
        [
            new("src_order_id", "order_id"),
            new("src_customer", "customer_code"),
        ];
        IReadOnlyList<PgOutputCapturedChange> changes =
        [
            // Intra-batch insert then delete -> retains full remapped payload for NOT NULL safety under SoftDelete
            new("public", "orders", "insert", "0/20", now, new Dictionary<string, object?> { ["src_order_id"] = 10L, ["src_customer"] = "ACME", ["ignored_col"] = "x" }, null),
            new("public", "orders", "delete", "0/21", now.AddSeconds(2), null, new Dictionary<string, object?> { ["src_order_id"] = 10L }),
            // Out-of-batch delete under REPLICA IDENTITY DEFAULT -> HasFullPayload=false -> targeted tombstone UPDATE
            new("public", "orders", "delete", "0/22", now.AddSeconds(3), null, new Dictionary<string, object?> { ["src_order_id"] = 11L }),
            // Delete followed by resurrection insert
            new("public", "orders", "delete", "0/23", now.AddSeconds(4), null, new Dictionary<string, object?> { ["src_order_id"] = 12L }),
            new("public", "orders", "insert", "0/24", now.AddSeconds(5), new Dictionary<string, object?> { ["src_order_id"] = 12L, ["src_customer"] = "PHOENIX" }, null),
        ];

        var coalesced = CdcRelationalMirrorRowProjector.CoalesceChangesByKey(
            changes,
            ["order_id"],
            CdcDeletePolicy.SoftDelete,
            mappings);

        coalesced.Count.ShouldBe(3);

        coalesced[0].IsDelete.ShouldBeTrue();
        coalesced[0].HasFullPayload.ShouldBeTrue();
        coalesced[0].Data!.ContainsKey("ignored_col").ShouldBeFalse();
        coalesced[0].Data!["customer_code"].ShouldBe("ACME");

        coalesced[1].IsDelete.ShouldBeTrue();
        coalesced[1].HasFullPayload.ShouldBeFalse();
        coalesced[1].KeyValues[0].ShouldBe(11L);

        coalesced[2].IsDelete.ShouldBeFalse();
        coalesced[2].HasFullPayload.ShouldBeTrue();
        coalesced[2].Data!["customer_code"].ShouldBe("PHOENIX");
    }

    [Fact]
    public void EvaluateCompatibility_Should_Suggest_Mappings_And_Validate_SoftDelete_Contract()
    {
        List<PreflightColumnDto> sourceCols =
        [
            new() { Name = "src_order_id", DataType = "bigint", IsNullable = false },
            new() { Name = "CustomerCode", DataType = "character varying", IsNullable = false },
            new() { Name = "amount", DataType = "numeric", IsNullable = false },
        ];
        List<PreflightColumnDto> targetCols =
        [
            new() { Name = "order_id", DataType = "bigint", IsNullable = false, IsUniqueIndexed = true },
            new() { Name = "customer_code", DataType = "text", IsNullable = false },
            new() { Name = "amount", DataType = "numeric", IsNullable = false },
            new() { Name = "is_deleted", DataType = "boolean", IsNullable = false },
            new() { Name = "deleted_at_utc", DataType = "timestamp with time zone", IsNullable = false },
        ];

        var suggested = CdcRelationalMirrorPreflightService.BuildSuggestedMappings(sourceCols, targetCols);
        suggested.ShouldContain(m => m.SourceColumn == "CustomerCode" && m.TargetColumn == "customer_code");
        suggested.ShouldContain(m => m.SourceColumn == "amount" && m.TargetColumn == "amount");

        var issues = new List<PreflightSchemaIssueDto>();
        CdcRelationalMirrorPreflightService.EvaluateCompatibility(
            "public.orders_mirror",
            sourceCols,
            targetCols,
            [new("src_order_id", "order_id"), new("CustomerCode", "customer_code"), new("amount", "amount")],
            ["order_id"],
            CdcDeletePolicy.SoftDelete,
            "is_deleted",
            "deleted_at_utc",
            issues);

        issues.ShouldContain(i => i.Code == "SwapMid:ETL:SoftDeleteTimestampNotNullable" && i.Severity == "error");

        targetCols[4] = new() { Name = "deleted_at_utc", DataType = "timestamp with time zone", IsNullable = true };
        issues.Clear();
        CdcRelationalMirrorPreflightService.EvaluateCompatibility(
            "public.orders_mirror",
            sourceCols,
            targetCols,
            [new("src_order_id", "order_id"), new("CustomerCode", "customer_code"), new("amount", "amount")],
            ["order_id"],
            CdcDeletePolicy.SoftDelete,
            "is_deleted",
            "deleted_at_utc",
            issues);

        issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeliverAsync_Should_Reject_Multiple_Monitored_Tables_Or_Missing_KeyColumns()
    {
        var targetSource = new DataSource(
            Guid.NewGuid(),
            "pg-mirror-target",
            DataSourceType.RelationalDatabase,
            """{"provider":"postgresql","host":"127.0.0.1","port":5432,"database":"swapmid"}""");
        var change = new PgOutputCapturedChange(
            "public", "orders", "insert", "0/10", DateTime.UtcNow,
            new Dictionary<string, object?> { ["order_id"] = 1, ["amount"] = 10 }, null);

        var multiTableStream = new CdcStreamDefinition(
            Guid.NewGuid(), "s1", Guid.NewGuid(), "pub1", "slot1", CdcTargetMode.RelationalTable,
            monitoredTables: "public.orders,public.items",
            targetSinkId: targetSource.Id, targetSinkDestination: "public.orders_mirror", targetSinkKeyColumns: "order_id");

        var exMulti = await Should.ThrowAsync<BusinessException>(() =>
            CdcRelationalMirrorDeliveryAdapter.DeliverAsync(
                multiTableStream, targetSource, [change], new ThrowingConnectionFactory(), NullLogger.Instance, CancellationToken.None));
        exMulti.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);

        var noKeyStream = new CdcStreamDefinition(
            Guid.NewGuid(), "s2", Guid.NewGuid(), "pub2", "slot2", CdcTargetMode.RelationalTable,
            monitoredTables: "public.orders",
            targetSinkId: targetSource.Id, targetSinkDestination: "public.orders_mirror", targetSinkKeyColumns: null);

        var exKey = await Should.ThrowAsync<BusinessException>(() =>
            CdcRelationalMirrorDeliveryAdapter.DeliverAsync(
                noKeyStream, targetSource, [change], new ThrowingConnectionFactory(), NullLogger.Instance, CancellationToken.None));
        exKey.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid);
    }

    [Fact]
    public void CdcMirrorValueCoercer_Should_Parse_PgOutput_Text_Values_To_Exact_Clr_Types()
    {
        CdcMirrorValueCoercer.Coerce("42", "bigint").ShouldBe(42L);
        CdcMirrorValueCoercer.Coerce("123.45", "numeric(14,2)").ShouldBe(123.45m);
        CdcMirrorValueCoercer.Coerce("t", "boolean").ShouldBe(true);
        CdcMirrorValueCoercer.Coerce("f", "boolean").ShouldBe(false);
        var guid = Guid.NewGuid();
        CdcMirrorValueCoercer.Coerce(guid.ToString(), "uuid").ShouldBe(guid);
        CdcMirrorValueCoercer.Coerce("2026-11-17", "date").ShouldBe(new DateOnly(2026, 11, 17));
        CdcMirrorValueCoercer.Coerce("\\xdeadbeef", "bytea").ShouldBe(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        var ts = CdcMirrorValueCoercer.Coerce("2026-11-17 15:30:00+00", "timestamp with time zone");
        ts.ShouldBeOfType<DateTime>().Kind.ShouldBe(DateTimeKind.Utc);
    }

    private sealed class ThrowingConnectionFactory : IEtlRelationalConnectionFactory
    {
        public DbConnection Create(string configJson, int connectTimeoutSeconds)
            => throw new InvalidOperationException("Should not open connection on invalid config.");
    }
}
