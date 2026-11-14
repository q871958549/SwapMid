using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.DeveloperDocs;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Content;

namespace SwapMid.Controllers.DeveloperDocs;

[RemoteService]
[Area("app")]
[Route("api/v1/db-schema")]
public class DbSchemaController : SwapMidController, IDbSchemaDictionaryAppService
{
    private readonly IDbSchemaDictionaryAppService _service;

    public DbSchemaController(IDbSchemaDictionaryAppService service)
    {
        _service = service;
    }

    [HttpGet("tables")]
    public Task<List<DbTableSummaryDto>> GetTablesAsync(string? filter = null, string? domainGroup = null)
        => _service.GetTablesAsync(filter, domainGroup);

    [HttpGet("tables/{tableName}")]
    public Task<DbTableDto> GetTableAsync(string tableName)
        => _service.GetTableAsync(tableName);

    [HttpGet("export/excel")]
    public Task<IRemoteStreamContent> ExportExcelAsync(string? domainGroup = null)
        => _service.ExportExcelAsync(domainGroup);

    [HttpGet("export/markdown")]
    public Task<IRemoteStreamContent> ExportMarkdownAsync(string? domainGroup = null)
        => _service.ExportMarkdownAsync(domainGroup);

    // ===== 阶段二：外部业务库 =====

    [HttpGet("external/sources")]
    public Task<List<ExternalDbSourceDto>> GetExternalSourcesAsync()
        => _service.GetExternalSourcesAsync();

    [HttpGet("external/{sourceKey}/tables")]
    public Task<List<DbTableSummaryDto>> GetExternalTablesAsync(string sourceKey, string? filter = null)
        => _service.GetExternalTablesAsync(sourceKey, filter);

    [HttpGet("external/{sourceKey}/tables/{tableName}")]
    public Task<DbTableDto> GetExternalTableAsync(string sourceKey, string tableName)
        => _service.GetExternalTableAsync(sourceKey, tableName);
}
