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

    [HttpGet] public Task<PagedResultDto<DataSourceDto>> GetListAsync(GetDataSourceListInput input) => _service.GetListAsync(input);
    [HttpGet("{id}")] public Task<DataSourceDto> GetAsync(Guid id) => _service.GetAsync(id);
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Create)]
    [HttpPost] public Task<DataSourceDto> CreateAsync(DataSourceCreateUpdateDto input) => _service.CreateAsync(input);
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Update)]
    [HttpPut("{id}")] public Task<DataSourceDto> UpdateAsync(Guid id, DataSourceCreateUpdateDto input) => _service.UpdateAsync(id, input);
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Delete)]
    [HttpDelete("{id}")] public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Create)]
    [HttpPost("test-connection")] public Task<TestConnectionResultDto> TestConnectionAsync(DataSourceTestConnectionInput input) => _service.TestConnectionAsync(input);
    [Authorize(SwapMidPermissions.DataIntegration.DataSources.Update)]
    [HttpPost("{id}/test-connection")] public Task<TestConnectionResultDto> TestConnectionByIdAsync(Guid id, DataSourceTestConnectionInput input) => _service.TestConnectionByIdAsync(id, input);
}
