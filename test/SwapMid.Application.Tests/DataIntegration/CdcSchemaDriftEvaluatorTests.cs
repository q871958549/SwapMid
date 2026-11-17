using System;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

public class CdcSchemaDriftEvaluatorTests
{
    [Fact]
    public void Should_Compute_Deterministic_Hash_Regardless_Of_Table_Order()
    {
        var schemaA = new CdcCapturedTableSchema(
            "public",
            "orders",
            [
                new("order_id", "bigint", false),
                new("total_amount", "numeric(18,2)", false),
            ]);
        var schemaB = new CdcCapturedTableSchema(
            "public",
            "customers",
            [
                new("customer_id", "bigint", false),
                new("name", "text", true),
            ]);

        var (hash1, json1) = CdcSchemaDriftEvaluator.SerializeBaseline([schemaA, schemaB]);
        var (hash2, json2) = CdcSchemaDriftEvaluator.SerializeBaseline([schemaB, schemaA]);

        hash1.ShouldBe(hash2);
        json1.ShouldBe(json2);

        var eval = CdcSchemaDriftEvaluator.Compare(json1, [schemaB, schemaA]);
        eval.HasDrift.ShouldBeFalse();
        eval.CurrentHash.ShouldBe(hash1);
    }

    [Fact]
    public void Should_Detect_Added_Removed_Modified_Columns_And_Missing_Table()
    {
        var baseline = new CdcCapturedTableSchema(
            "public",
            "orders",
            [
                new("order_id", "bigint", false),
                new("total_amount", "numeric(12,2)", false),
                new("legacy_note", "text", true),
            ]);
        var (_, baselineJson) = CdcSchemaDriftEvaluator.SerializeBaseline([baseline]);

        var drifted = new CdcCapturedTableSchema(
            "public",
            "orders",
            [
                new("order_id", "bigint", false),
                new("total_amount", "numeric(16,2)", true),
                new("loyalty_tier", "character varying(32)", true),
            ]);

        var eval = CdcSchemaDriftEvaluator.Compare(baselineJson, [drifted]);
        eval.HasDrift.ShouldBeTrue();
        eval.Summary.ShouldContain("public.orders:");
        eval.Summary.ShouldContain("-col legacy_note");
        eval.Summary.ShouldContain("+col loyalty_tier (character varying(32))");
        eval.Summary.ShouldContain("~col total_amount (numeric(12,2)->numeric(16,2)?)");

        var missingTableEval = CdcSchemaDriftEvaluator.Compare(baselineJson, []);
        missingTableEval.HasDrift.ShouldBeTrue();
        missingTableEval.Summary.ShouldContain("public.orders");
    }

    [Fact]
    public void Should_Respect_MaxSchemaDriftSummaryLength_Bound()
    {
        var baseCols = new CdcCapturedTableColumn[50];
        var nextCols = new CdcCapturedTableColumn[50];
        for (var i = 0; i < 50; i++)
        {
            baseCols[i] = new CdcCapturedTableColumn($"column_long_name_{i:000}", "text", true);
            nextCols[i] = new CdcCapturedTableColumn($"new_column_long_name_{i:000}", "bigint", false);
        }

        var (_, baselineJson) = CdcSchemaDriftEvaluator.SerializeBaseline(
            [new CdcCapturedTableSchema("public", "very_wide_table", baseCols)]);
        var eval = CdcSchemaDriftEvaluator.Compare(
            baselineJson,
            [new CdcCapturedTableSchema("public", "very_wide_table", nextCols)]);

        eval.HasDrift.ShouldBeTrue();
        eval.Summary.Length.ShouldBeLessThanOrEqualTo(CdcStreamConsts.MaxSchemaDriftSummaryLength);
        eval.Summary.ShouldEndWith("...");
    }
}
