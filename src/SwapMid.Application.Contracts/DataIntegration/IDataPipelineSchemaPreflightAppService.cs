using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>连接器能力目录与数据管道源/目标 Schema 静态兼容性预检服务契约。</summary>
public interface IDataPipelineSchemaPreflightAppService : IApplicationService
{
    /// <summary>读取平台内置连接器能力注册表。</summary>
    Task<List<ConnectorCapabilityDto>> GetCapabilitiesAsync();

    /// <summary>在不搬运或改写业务数据的前提下，静态探测源查询列结构与目标表约束兼容性。</summary>
    [UnitOfWork(IsDisabled = true)]
    Task<PipelineSchemaPreflightResultDto> PreflightSchemaAsync(
        PreflightPipelineSchemaInput input,
        CancellationToken cancellationToken = default);
}
