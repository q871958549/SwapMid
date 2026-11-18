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

    /// <summary>查询平台主库实时数据库表目录概览。</summary>
    [HttpGet("tables")]
    public Task<List<DbTableSummaryDto>> GetTablesAsync(string? filter = null, string? domainGroup = null)
        => _service.GetTablesAsync(filter, domainGroup);

    /// <summary>获取平台主库单张物理表的列、主键、索引及外键字典详情。</summary>
    [HttpGet("tables/{tableName}")]
    public Task<DbTableDto> GetTableAsync(string tableName)
        => _service.GetTableAsync(tableName);

    /// <summary>导出平台主库数据库表字典为 Excel 工作簿。</summary>
    [HttpGet("export/excel")]
    public Task<IRemoteStreamContent> ExportExcelAsync(string? domainGroup = null)
        => _service.ExportExcelAsync(domainGroup);

    /// <summary>导出平台主库数据库表字典为 Markdown 文档。</summary>
    [HttpGet("export/markdown")]
    public Task<IRemoteStreamContent> ExportMarkdownAsync(string? domainGroup = null)
        => _service.ExportMarkdownAsync(domainGroup);

    // ===== 阶段二：外部业务库 =====

    /// <summary>列出支持表结构字典探测的已配置外部数据库源。</summary>
    [HttpGet("external/sources")]
    public Task<List<ExternalDbSourceDto>> GetExternalSourcesAsync()
        => _service.GetExternalSourcesAsync();

    /// <summary>探测外部数据源指定库内的表目录清单。</summary>
    [HttpGet("external/{sourceKey}/tables")]
    public Task<List<DbTableSummaryDto>> GetExternalTablesAsync(string sourceKey, string? filter = null)
        => _service.GetExternalTablesAsync(sourceKey, filter);

    /// <summary>探测外部数据源单张物理表的列结构与主键约束详情。</summary>
    [HttpGet("external/{sourceKey}/tables/{tableName}")]
    public Task<DbTableDto> GetExternalTableAsync(string sourceKey, string tableName)
        => _service.GetExternalTableAsync(sourceKey, tableName);
}
