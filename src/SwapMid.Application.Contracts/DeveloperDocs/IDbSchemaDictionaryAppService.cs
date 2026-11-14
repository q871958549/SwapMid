using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace SwapMid.DeveloperDocs;

/// <summary>
/// 数据库表字典：查看平台业务表的结构与中文含义（来自 EF 模型 + 实体 XML 注释），并导出 Excel / Markdown。
/// 只读；归「开发者文档」域，挂 <c>DeveloperDocs.DbDictionary</c> 权限。
/// </summary>
public interface IDbSchemaDictionaryAppService : IApplicationService
{
    /// <summary>表列表（可按关键字过滤表名/中文名、按业务域过滤）。</summary>
    Task<List<DbTableSummaryDto>> GetTablesAsync(string? filter = null, string? domainGroup = null);

    /// <summary>单表字段明细。</summary>
    Task<DbTableDto> GetTableAsync(string tableName);

    /// <summary>导出 Excel（可按业务域筛选；不传=全部）。</summary>
    Task<IRemoteStreamContent> ExportExcelAsync(string? domainGroup = null);

    /// <summary>导出 Markdown（可按业务域筛选；不传=全部）。</summary>
    Task<IRemoteStreamContent> ExportMarkdownAsync(string? domainGroup = null);

    // ===== 阶段二：外部业务库（只读元数据，中文取 DB 自带 COMMENT / 扩展属性）=====

    /// <summary>外部数据源列表（ERP/CRM/WMS 等外部业务库）。</summary>
    Task<List<ExternalDbSourceDto>> GetExternalSourcesAsync();

    /// <summary>某外部库的表列表（可按关键字过滤表名/注释）。</summary>
    Task<List<DbTableSummaryDto>> GetExternalTablesAsync(string sourceKey, string? filter = null);

    /// <summary>某外部库单表字段明细。</summary>
    Task<DbTableDto> GetExternalTableAsync(string sourceKey, string tableName);
}
