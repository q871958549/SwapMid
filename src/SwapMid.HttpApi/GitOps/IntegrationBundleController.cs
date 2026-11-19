using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwapMid.Permissions;
using Volo.Abp;

namespace SwapMid.Controllers.GitOps;

/// <summary>声明式 GitOps 集成清单接口（零密钥导出、只读 Diff 计划、受控 Dry-Run/Apply）</summary>
[RemoteService]
[Area("app")]
[Route("api/v1/integration-bundles")]
public class IntegrationBundleController : SwapMidController
{
    private readonly IntegrationBundleOrchestrator _orchestrator;

    public IntegrationBundleController(IntegrationBundleOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>导出当前平台声明式 GitOps 集成清单（YAML / JSON，零明文密钥）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Default)]
    [HttpGet("export")]
    public Task<IntegrationBundleExportResultDto> ExportAsync([FromQuery] string? format = "yaml")
        => _orchestrator.ExportAsync(User, format);

    /// <summary>只读对比声明式 YAML / JSON 集成清单与当前平台实时配置差异</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Default)]
    [HttpPost("diff")]
    public Task<IntegrationBundleDiffResultDto> DiffAsync([FromBody] IntegrationBundleDiffInput input)
        => _orchestrator.DiffAsync(User, input.Content);

    /// <summary>受控应用声明式 YAML / JSON 集成清单（默认 Dry-Run，新建管道与 CDC 强制 Draft）</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataPipelines.Create)]
    [HttpPost("apply")]
    public Task<IntegrationBundleApplyResultDto> ApplyAsync([FromBody] IntegrationBundleApplyInput input)
        => _orchestrator.ApplyAsync(User, input);
}
