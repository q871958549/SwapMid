using System.Text.Json;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 转换配置结构解析测试(EtlTransformConfig.Parse)——纯函数，守护 targetTable 标识符与
/// mode 契约；sourceQuery 的方言感知安全边界由 EtlSourceQueryGuard 在执行前复核。
/// </summary>
public class EtlTransformConfigTests
{
    [Fact]
    public void 正常配置_解析出查询_表名_模式()
    {
        var cfg = EtlTransformConfig.Parse("""{"sourceQuery":"SELECT id FROM a","targetTable":"dbo.b","mode":"truncate"}""");
        cfg.SourceQuery.ShouldBe("SELECT id FROM a");
        cfg.TargetTable.ShouldBe("dbo.b");
        cfg.Mode.ShouldBe(EtlWriteMode.Truncate);
    }

    [Fact]
    public void 未指定模式_默认append()
    {
        var config = EtlTransformConfig.Parse(
            """{"sourceQuery":"select 1 as x","targetTable":"t"}""");
        config.Mode.ShouldBe(EtlWriteMode.Append);
        config.ReconciliationStrategy.ShouldBe(
            EtlReconciliationStrategy.ManualV0);
    }

    [Theory]
    [InlineData("append", EtlWriteMode.Append,
        EtlReconciliationStrategy.ManualV0)]
    [InlineData("truncate", EtlWriteMode.Truncate,
        EtlReconciliationStrategy.ManualV0)]
    [InlineData("append-receipt-v1", EtlWriteMode.Append,
        EtlReconciliationStrategy.TargetReceiptV1)]
    [InlineData("truncate-receipt-v1", EtlWriteMode.Truncate,
        EtlReconciliationStrategy.TargetReceiptV1)]
    public void 四种模式必须显式映射写语义与对账协议(
        string mode,
        EtlWriteMode expectedWriteMode,
        EtlReconciliationStrategy expectedStrategy)
    {
        var config = EtlTransformConfig.Parse(
            $$"""{"sourceQuery":"SELECT 1","targetTable":"t","mode":"{{mode}}"}""");

        config.Mode.ShouldBe(expectedWriteMode);
        config.ReconciliationStrategy.ShouldBe(expectedStrategy);
    }

    [Theory]
    [InlineData("upsert")]
    [InlineData("append-receipt-v2")]
    [InlineData("receipt-v1")]
    public void 未知模式必须失败关闭防止新协议被旧语义静默降级(string mode)
        => Should.Throw<BusinessException>(() =>
            EtlTransformConfig.Parse(
                $$"""{"sourceQuery":"SELECT 1","targetTable":"t","mode":"{{mode}}"}"""));

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("true")]
    public void 只有缺失mode才兼容为append_显式空值或非字符串必须失败关闭(
        string modeJson)
        => Should.Throw<BusinessException>(() =>
            EtlTransformConfig.Parse(
                $$"""{"sourceQuery":"SELECT 1","targetTable":"t","mode":{{modeJson}}}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空配置_报错(string? json)
        => Should.Throw<BusinessException>(() => EtlTransformConfig.Parse(json));

    [Fact]
    public void 缺sourceQuery_报错()
        => Should.Throw<BusinessException>(() => EtlTransformConfig.Parse("""{"targetTable":"t"}"""));

    [Fact]
    public void 源查询非SELECT_由最终执行边界词法守卫拒绝()
    {
        var delete = EtlTransformConfig.Parse(
            """{"sourceQuery":"DELETE FROM a","targetTable":"t"}""");
        var update = EtlTransformConfig.Parse(
            """{"sourceQuery":"UPDATE a SET x=1","targetTable":"t"}""");

        Should.Throw<BusinessException>(
            () => EtlSourceQueryGuard.Validate(delete.SourceQuery, "sqlserver"));
        Should.Throw<BusinessException>(
            () => EtlSourceQueryGuard.Validate(update.SourceQuery, "mysql"));
    }

    [Theory]
    [InlineData("users; DROP TABLE x")]
    [InlineData("a-b")]
    [InlineData("'; DELETE")]
    [InlineData("a.b.c")]
    public void 非法目标表名_报错_防注入(string table)
        => Should.Throw<BusinessException>(() =>
            EtlTransformConfig.Parse($$"""{"sourceQuery":"SELECT 1","targetTable":"{{table}}"}"""));

    [Fact]
    public void 合法表名_含schema或纯表_通过()
    {
        EtlTransformConfig.Parse("""{"sourceQuery":"SELECT 1","targetTable":"dbo.my_table"}""").TargetTable.ShouldBe("dbo.my_table");
        EtlTransformConfig.Parse("""{"sourceQuery":"SELECT 1","targetTable":"plain_table"}""").TargetTable.ShouldBe("plain_table");
    }

    [Fact]
    public void 畸形JSON_只返回稳定错误码且不回显输入()
    {
        const string secret = "P1_SECRET_SENTINEL";
        var malformed = "{\"sourceQuery\":\"SELECT 1\",\"password\":\"" + secret + "\"";

        var exception = Should.Throw<BusinessException>(
            () => EtlTransformConfig.Parse(malformed));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        exception.Message.ShouldNotContain(secret);
        JsonSerializer.Serialize(exception.Data).ShouldNotContain(secret);
    }
}
