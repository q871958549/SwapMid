using Shouldly;
using Xunit;

namespace SwapMid.SystemManagement;

public class AuditLogParameterDisclosurePolicyTests
{
    private const string HistoricalSecret =
        """{"input":{"config":{"password":"historical-secret"}}}""";

    [Theory]
    [InlineData("SwapMid.DataIntegration.DataSourceAppService")]
    [InlineData("SwapMid.DataIntegration.IDataSourceAppService")]
    [InlineData("SwapMid.Controllers.DataIntegration.DataSourceController")]
    public void DataSource所有动作_历史参数固定隐藏(string serviceName)
    {
        var result = AuditLogParameterDisclosurePolicy.Protect(
            serviceName,
            HistoricalSecret);

        result.ShouldBe(AuditLogParameterDisclosurePolicy.HiddenParameters);
        result!.ShouldNotContain("historical-secret");
    }

    [Fact]
    public void 非DataSource动作_参数保持原样()
    {
        const string parameters = """{"id":"visible-id"}""";

        AuditLogParameterDisclosurePolicy.Protect(
                "SwapMid.DataIntegration.DataPipelineAppService",
                parameters)
            .ShouldBe(parameters);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Other.Namespace.DataSourceAppService")]
    [InlineData("SwapMid.DataIntegration.DataSourceController")]
    public void 非精确服务边界_不得误判(string? serviceName)
    {
        AuditLogParameterDisclosurePolicy.IsDataSourceAction(serviceName).ShouldBeFalse();
    }
}
