using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwapMid.Layout;
using Volo.Abp;

namespace SwapMid.Controllers.Layout;

/// <summary>当前用户的表格视图与行密度云端偏好端点。</summary>
[RemoteService]
[Area("app")]
[Authorize]
[Route("api/v1/table-preference")]
public class TablePreferenceController : SwapMidController, ITablePreferenceAppService
{
    private readonly ITablePreferenceAppService _service;

    public TablePreferenceController(ITablePreferenceAppService service)
    {
        _service = service;
    }

    /// <summary>读取当前登录用户的表格视图及行密度偏好快照。</summary>
    [HttpGet]
    public Task<TablePreferenceSnapshotDto> GetAsync()
        => _service.GetAsync();

    /// <summary>保存或清除单张表格的列序、显隐、列宽及命名预设偏好。</summary>
    [HttpPut("table")]
    public Task SaveTableViewAsync([FromBody] SaveTableViewPreferenceInput input)
        => _service.SaveTableViewAsync(input);

    /// <summary>保存当前用户的全局表格行密度偏好（standard / compact）。</summary>
    [HttpPut("density")]
    public Task SaveDensityAsync([FromBody] SaveTableDensityInput input)
        => _service.SaveDensityAsync(input);
}
