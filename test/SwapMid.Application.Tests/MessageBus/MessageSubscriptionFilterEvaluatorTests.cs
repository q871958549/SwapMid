using Shouldly;
using Xunit;

namespace SwapMid.MessageBus;

public class MessageSubscriptionFilterEvaluatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Filter_Should_Match_Everything(string? filterRule)
    {
        MessageSubscriptionFilterEvaluator.IsMatch(
                filterRule,
                "pipeline.execution.succeeded",
                "{\"status\":\"Succeeded\"}")
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("order.shipped", "order.shipped:ORD-001", "{\"amount\":100}", true)]
    [InlineData("ORD-001", "order.created", "{\"orderNo\":\"ord-001\"}", true)]
    [InlineData("invoice", "order.shipped:ORD-001", "{\"amount\":100}", false)]
    public void Plain_Substring_Filter_Should_Remain_Backward_Compatible(
        string filterRule,
        string subject,
        string payload,
        bool expected)
    {
        MessageSubscriptionFilterEvaluator.IsMatch(filterRule, subject, payload)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("subject:pipeline.execution.*", "pipeline.execution.succeeded", true)]
    [InlineData("subject:PIPELINE.EXECUTION.*", "pipeline.execution.failed", true)]
    [InlineData("subject:order.*:ORD-???", "order.shipped:ORD-009", true)]
    [InlineData("subject:order.*:ORD-???", "order.shipped:ORD-0009", false)]
    [InlineData("subject:pipeline.execution.failed", "pipeline.execution.succeeded", false)]
    public void Subject_Glob_Filter_Should_Support_Wildcards(
        string filterRule,
        string subject,
        bool expected)
    {
        MessageSubscriptionFilterEvaluator.IsMatch(filterRule, subject, "{}")
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("$.status=Succeeded", "{\"status\":\"Succeeded\",\"processedRows\":3}", true)]
    [InlineData("$.status=\"succeeded\"", "{\"status\":\"Succeeded\"}", true)]
    [InlineData("$.processedRows=3", "{\"processedRows\":3}", true)]
    [InlineData("$.order.tier=VIP", "{\"order\":{\"tier\":\"vip\"}}", true)]
    [InlineData("$.active=true", "{\"active\":true}", true)]
    [InlineData("$.errorCode!=null", "{\"errorCode\":\"ETL_TARGET_UNAVAILABLE\"}", true)]
    [InlineData("$.errorCode!=null", "{\"errorCode\":null}", false)]
    [InlineData("$.errorCode!=null", "{\"status\":\"Succeeded\"}", false)]
    [InlineData("$.status!=Failed", "{\"status\":\"Succeeded\"}", true)]
    [InlineData("$.status=Succeeded", "not-valid-json", false)]
    [InlineData("$.status!=Failed", "not-valid-json", false)]
    public void Json_Path_Filter_Should_Safely_Match_Scalar_Fields(
        string filterRule,
        string payload,
        bool expected)
    {
        MessageSubscriptionFilterEvaluator.IsMatch(
                filterRule,
                "pipeline.execution.succeeded",
                payload)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData(
        "subject:pipeline.execution.*; $.status=Succeeded; $.processedRows!=0",
        "pipeline.execution.succeeded",
        "{\"status\":\"Succeeded\",\"processedRows\":3}",
        true)]
    [InlineData(
        "subject:pipeline.execution.*; $.status=Succeeded; $.processedRows!=0",
        "pipeline.execution.succeeded",
        "{\"status\":\"Succeeded\",\"processedRows\":0}",
        false)]
    [InlineData(
        "subject:pipeline.execution.*; $.status=Failed",
        "pipeline.execution.succeeded",
        "{\"status\":\"Succeeded\"}",
        false)]
    public void Multi_Clause_Filter_Should_Combine_With_Logical_And(
        string filterRule,
        string subject,
        string payload,
        bool expected)
    {
        MessageSubscriptionFilterEvaluator.IsMatch(filterRule, subject, payload)
            .ShouldBe(expected);
    }
}
