using System;
using System.Threading.Tasks;
using SwapMid.DataIntegration;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace SwapMid.Controllers.DataIntegration;

[RemoteService]
[Area("app")]
[Route("api/v1/data-sources")]
public class DataSourceController : SwapMidController, IDataSourceAppService
{
    private readonly IDataSourceAppService _service;
    public DataSourceController(IDataSourceAppService service) { _service = service; }

    /// <summary>分页查询数据集成数据源及外部接收端列表（凭证字段脱敏）。</summary>
    [HttpGet] public Task<PagedResultDto<DataSourceDto>> GetListAsync(GetDataSourceListInput input) => _service.GetListAsync(input);
    /// <summary>获取单个数据源配置详情。</summary>
    [HttpGet("{id}")] public Task<DataSourceDto> GetAsync(Guid id) => _service.GetAsync(id);
    /// <summary>创建数据源连接配置。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Create)]
    [HttpPost] public Task<DataSourceDto> CreateAsync(DataSourceCreateUpdateDto input) => _service.CreateAsync(input);
    /// <summary>更新数据源连接配置（修改范围受密码凭证重填校验约束）。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Update)]
    [HttpPut("{id}")] public Task<DataSourceDto> UpdateAsync(Guid id, DataSourceCreateUpdateDto input) => _service.UpdateAsync(id, input);
    /// <summary>删除指定数据源。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Delete)]
    [HttpDelete("{id}")] public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
    /// <summary>基于提交的配置参数探测上游数据库或外部接收端连通性。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Create)]
    [HttpPost("test-connection")] public Task<TestConnectionResultDto> TestConnectionAsync(DataSourceTestConnectionInput input) => _service.TestConnectionAsync(input);
    /// <summary>使用已保存密钥合并探测指定数据源的实时连通性。</summary>
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Update)]
    [HttpPost("{id}/test-connection")] public Task<TestConnectionResultDto> TestConnectionByIdAsync(Guid id, DataSourceTestConnectionInput input) => _service.TestConnectionByIdAsync(id, input);
}
