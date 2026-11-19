using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwapMid.Permissions;
using Volo.Abp;

namespace SwapMid.Controllers.Lineage;

/// <summary>跨引擎实时数据血缘图谱与变更影响面分析接口（零密钥、只读聚合）</summary>
[RemoteService]
[Area("app")]
[Route("api/v1/integration-lineage")]
public class IntegrationLineageController : SwapMidController
{
    private readonly IntegrationLineageBuilder _builder;

    public IntegrationLineageController(IntegrationLineageBuilder builder)
    {
        _builder = builder;
    }

    /// <summary>构建当前平台全量跨引擎数据血缘 DAG（数据源、ETL 管道、CDC 实时流、开放湖仓表、消息主题、订阅与 API 网关路由）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Default)]
    [HttpGet]
    public Task<IntegrationLineageGraphDto> GetGraphAsync()
        => _builder.BuildGraphAsync(User);

    /// <summary>分析指定节点或关键字的上下游变更影响范围（Blast Radius）、传播路径与风险分级</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Default)]
    [HttpGet("impact")]
    public Task<IntegrationLineageImpactDto> AnalyzeImpactAsync(
        [FromQuery] string? nodeId = null,
        [FromQuery] string? query = null,
        [FromQuery] string? direction = "both",
        [FromQuery] int maxDepth = 4)
        => _builder.AnalyzeImpactAsync(User, nodeId, query, direction, maxDepth);

    /// <summary>导出 CNCF OpenLineage 2.0（v2-0-2）标准血缘 JSON（含字段级 columnLineage 与 schema facets，兼容 Marquez / DataHub / OpenMetadata）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Default)]
    [HttpGet("openlineage")]
    public Task<OpenLineageExportDto> ExportOpenLineageAsync([FromQuery] string? ns = null)
        => _builder.BuildOpenLineageExportAsync(User, ns);
}
