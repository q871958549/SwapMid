using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.DataIntegration;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Uow;

namespace SwapMid.Controllers.DataIntegration;

/// <summary>
/// 数据管道连接器能力目录与源/目标 Schema 静态兼容性预检控制器。
/// </summary>
[RemoteService]
[Area("app")]
[Route("api/v1/data-pipelines")]
[Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Default)]
public class DataPipelineSchemaPreflightController
    : SwapMidController, IDataPipelineSchemaPreflightAppService
{
    private readonly IDataPipelineSchemaPreflightAppService _service;

    public DataPipelineSchemaPreflightController(IDataPipelineSchemaPreflightAppService service)
    {
        _service = service;
    }

    /// <summary>获取平台内置数据源连接器的读写、原子 Upsert 与机器回执能力清单。</summary>
    [HttpGet("capabilities")]
    public Task<List<ConnectorCapabilityDto>> GetCapabilitiesAsync()
        => _service.GetCapabilitiesAsync();

    /// <summary>静态探测源查询结果列与目标表字段、非空及唯一键约束的兼容性（不写入任何业务数据）。</summary>
    [HttpPost("preflight-schema")]
    [UnitOfWork(IsDisabled = true)]
    public Task<PipelineSchemaPreflightResultDto> PreflightSchemaAsync(
        [FromBody] PreflightPipelineSchemaInput input,
        CancellationToken cancellationToken = default)
        => _service.PreflightSchemaAsync(input, cancellationToken);
}
