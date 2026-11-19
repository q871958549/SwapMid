using System;
using System.Collections.Generic;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class CdcDataContractEvaluatorTests
{
    private static CdcStreamDefinition CreateStream(bool activate = true)
    {
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-sla-stream",
            Guid.NewGuid(),
            "pub_orders",
            "slot_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "orders/delta",
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);
        if (activate)
        {
            stream.Activate();
        }

        return stream;
    }

    private static string BuildBaselineJson(params string[] columnNames)
    {
        var cols = new List<CdcCapturedTableColumn>(columnNames.Length);
        foreach (var col in columnNames)
        {
            cols.Add(new CdcCapturedTableColumn(col, "varchar", false));
        }

        return CdcSchemaDriftEvaluator.SerializeBaseline(
            [new CdcCapturedTableSchema("swapmid_cdc", "orders", cols)]).Json;
    }

    [Fact]
    public void EvaluateSnapshot_Should_Return_Unconfigured_When_No_Sla_Set()
    {
        var stream = CreateStream();
        var eval = CdcDataContractEvaluator.EvaluateSnapshot(stream, DateTime.UtcNow);
        eval.Status.ShouldBe(CdcContractStatus.Unconfigured);
        eval.Violated.ShouldBeFalse();
        eval.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void EvaluateSnapshot_Should_Return_Healthy_When_All_Sla_Constraints_Met()
    {
        var now = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        var stream = CreateStream();
        stream.ConfigureDataContract(
            maxFreshnessLagSeconds: 300,
            maxAllowedWalLagBytes: 50L * 1024 * 1024,
            requiredColumns: "id, order_no, amount");
        stream.UpdateSchemaBaseline("hash-1", BuildBaselineJson("id", "order_no", "amount", "status"));
        stream.MarkCaptureSucceeded("mysql-bin.000003:1200", 5, now.AddSeconds(-45));

        var eval = CdcDataContractEvaluator.EvaluateSnapshot(stream, now, walLagBytes: 10L * 1024 * 1024);

        eval.Status.ShouldBe(CdcContractStatus.Healthy);
        eval.Violated.ShouldBeFalse();
        eval.FreshnessLagSeconds.ShouldBe(45);
        eval.MissingRequiredColumns.ShouldBeEmpty();
        eval.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void EvaluateSnapshot_Should_Warn_At_Eighty_Percent_Freshness_And_Breach_Over_Limit()
    {
        var now = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        var stream = CreateStream();
        stream.ConfigureDataContract(maxFreshnessLagSeconds: 100, maxAllowedWalLagBytes: null, requiredColumns: null);
        stream.MarkCaptureSucceeded("0/100", 1, now.AddSeconds(-85));

        var warn = CdcDataContractEvaluator.EvaluateSnapshot(stream, now);
        warn.Status.ShouldBe(CdcContractStatus.Warning);
        warn.Violated.ShouldBeFalse();

        stream.MarkCaptureSucceeded("0/100", 1, now.AddSeconds(-150));
        var breach = CdcDataContractEvaluator.EvaluateSnapshot(stream, now);
        breach.Status.ShouldBe(CdcContractStatus.Breached);
        breach.Violated.ShouldBeTrue();
        breach.Violations.ShouldContain(v => v.Type == "freshness" && v.Severity == "error");
    }

    [Fact]
    public void EvaluateSnapshot_Should_Breach_On_Missing_RequiredColumns_WalBudget_And_SchemaDrift()
    {
        var now = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        var stream = CreateStream();
        stream.ConfigureDataContract(
            maxFreshnessLagSeconds: 600,
            maxAllowedWalLagBytes: 10L * 1024 * 1024,
            requiredColumns: "id,order_no,missing_col");
        stream.UpdateSchemaBaseline("hash-1", BuildBaselineJson("id", "order_no", "amount"));
        stream.MarkSchemaDriftDetected("orders removed column legacy_flag", now, pauseStream: false);
        stream.MarkCaptureSucceeded("0/200", 2, now.AddSeconds(-10));

        var eval = CdcDataContractEvaluator.EvaluateSnapshot(stream, now, walLagBytes: 25L * 1024 * 1024);

        eval.Status.ShouldBe(CdcContractStatus.Breached);
        eval.Violated.ShouldBeTrue();
        eval.MissingRequiredColumns.ShouldBe(["missing_col"]);
        eval.Violations.ShouldContain(v => v.Type == "required_columns" && v.Severity == "error");
        eval.Violations.ShouldContain(v => v.Type == "wal_lag" && v.Severity == "error");
        eval.Violations.ShouldContain(v => v.Type == "schema_drift" && v.Severity == "error");
    }

    [Fact]
    public void ConfigureDataContract_Should_Validate_Bounds_And_Identifiers()
    {
        var stream = CreateStream(activate: false);

        Should.Throw<BusinessException>(() =>
            stream.ConfigureDataContract(maxFreshnessLagSeconds: 5, maxAllowedWalLagBytes: null, requiredColumns: null));
        Should.Throw<BusinessException>(() =>
            stream.ConfigureDataContract(maxFreshnessLagSeconds: null, maxAllowedWalLagBytes: 1024, requiredColumns: null));
        Should.Throw<BusinessException>(() =>
            stream.ConfigureDataContract(maxFreshnessLagSeconds: null, maxAllowedWalLagBytes: null, requiredColumns: "id;drop"));

        stream.ConfigureDataContract(120, 2L * 1024 * 1024, " id , order_no , ID ");
        stream.MaxFreshnessLagSeconds.ShouldBe(120);
        stream.MaxAllowedWalLagBytes.ShouldBe(2L * 1024 * 1024);
        stream.RequiredColumns.ShouldBe("id,order_no");
    }
}
