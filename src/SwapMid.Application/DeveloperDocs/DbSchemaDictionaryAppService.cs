using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace SwapMid.DeveloperDocs;

/// <summary>
/// 数据库表字典 AppService：读 <see cref="ISchemaMetadataProvider"/>（EF 模型 + 实体 XML 注释反射）
/// 呈现平台业务表结构与中文含义，并导出 Excel / Markdown。只读。
/// </summary>
[Authorize(SwapMidPermissions.DeveloperDocs.DbDictionary.Default)]
public class DbSchemaDictionaryAppService : ApplicationService, IDbSchemaDictionaryAppService
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ISchemaMetadataProvider _provider;
    private readonly IExternalDbSchemaReader _externalReader;

    public DbSchemaDictionaryAppService(
        ISchemaMetadataProvider provider,
        IExternalDbSchemaReader externalReader)
    {
        _provider = provider;
        _externalReader = externalReader;
    }

    public async Task<List<DbTableSummaryDto>> GetTablesAsync(string? filter = null, string? domainGroup = null)
    {
        var tables = await _provider.GetTablesAsync();
        return tables
            .Where(t => Match(t, filter, domainGroup))
            .Select(t => new DbTableSummaryDto
            {
                Name = t.Name,
                DisplayName = t.DisplayName,
                DomainGroup = t.DomainGroup,
                ColumnCount = t.Columns.Count,
                Description = t.Description,
            })
            .ToList();
    }

    public async Task<DbTableDto> GetTableAsync(string tableName)
    {
        var tables = await _provider.GetTablesAsync();
        var table = tables.FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase))
            ?? throw new UserFriendlyException($"未找到数据库表「{tableName}」");
        return ToDto(table);
    }

    // ===== 阶段二：外部业务库（只读元数据，中文取 DB 自带 COMMENT/扩展属性）=====

    public Task<List<ExternalDbSourceDto>> GetExternalSourcesAsync()
        => Task.FromResult(_externalReader.GetSources().ToList());

    public async Task<List<DbTableSummaryDto>> GetExternalTablesAsync(string sourceKey, string? filter = null)
    {
        // 过滤下推给 reader（DB 侧按表名 / 注释筛 + 限量）：外部库不是平台自管 schema，
        // 不能像平台库那样先全量进内存缓存再筛。
        var tables = await _externalReader.ReadTablesAsync(sourceKey, filter);
        return tables
            .Select(t => new DbTableSummaryDto
            {
                Name = t.Name,
                DisplayName = t.DisplayName,
                DomainGroup = t.DomainGroup,
                ColumnCount = t.Columns.Count,
                Description = t.Description,
            })
            .ToList();
    }

    public async Task<DbTableDto> GetExternalTableAsync(string sourceKey, string tableName)
    {
        var table = await _externalReader.ReadTableAsync(sourceKey, tableName)
            ?? throw new UserFriendlyException($"外部库「{sourceKey}」中未找到表「{tableName}」（或未配置连接串）");
        return ToDto(table);
    }

    public async Task<IRemoteStreamContent> ExportExcelAsync(string? domainGroup = null)
    {
        var tables = (await _provider.GetTablesAsync())
            .Where(t => domainGroup == null || t.DomainGroup == domainGroup)
            .ToList();

        // 多 sheet：「目录」总览 + 「字段明细」全表字段平铺。
        var catalog = tables.Select(t => new Dictionary<string, object?>
        {
            ["表名"] = t.Name,
            ["中文名"] = t.DisplayName,
            ["业务域"] = t.DomainGroup,
            ["字段数"] = t.Columns.Count,
            ["说明"] = t.Description,
        }).ToList();

        var detail = tables.SelectMany(t => t.Columns.Select(c => new Dictionary<string, object?>
        {
            ["表名"] = t.Name,
            ["表中文名"] = t.DisplayName,
            ["字段"] = c.Name,
            ["类型"] = c.StoreType,
            ["可空"] = c.IsNullable ? "是" : "否",
            ["主键"] = c.IsPrimaryKey ? "✓" : "",
            ["外键"] = c.IsForeignKey ? (c.ForeignTable ?? "✓") : "",
            ["索引"] = c.IsIndexed ? "✓" : "",
            ["默认值"] = c.DefaultValue,
            ["中文含义"] = c.Comment,
        })).ToList();

        var sheets = new Dictionary<string, object>
        {
            ["目录"] = catalog,
            ["字段明细"] = detail,
        };

        var stream = new MemoryStream();
        var config = new MiniExcelLibs.OpenXml.OpenXmlConfiguration { FastMode = true, EnableAutoWidth = true };
        await MiniExcelLibs.MiniExcel.SaveAsAsync(stream, sheets, excelType: MiniExcelLibs.ExcelType.XLSX, configuration: config);
        stream.Position = 0;
        return new RemoteStreamContent(stream, BuildFileName(domainGroup, "xlsx"), XlsxMime);
    }

    public async Task<IRemoteStreamContent> ExportMarkdownAsync(string? domainGroup = null)
    {
        var tables = (await _provider.GetTablesAsync())
            .Where(t => domainGroup == null || t.DomainGroup == domainGroup)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# 数据库表字典");
        sb.AppendLine();
        foreach (var group in tables.GroupBy(t => t.DomainGroup))
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (var t in group)
            {
                var title = string.IsNullOrEmpty(t.DisplayName) || t.DisplayName == t.Name
                    ? t.Name
                    : $"{t.Name}（{t.DisplayName}）";
                sb.AppendLine($"### {title}");
                if (!string.IsNullOrEmpty(t.Description)) sb.AppendLine($"> {t.Description}");
                sb.AppendLine();
                sb.AppendLine("| 字段 | 类型 | 可空 | 主键 | 外键 | 索引 | 默认值 | 中文含义 |");
                sb.AppendLine("|------|------|------|------|------|------|--------|----------|");
                foreach (var c in t.Columns)
                {
                    sb.AppendLine($"| {c.Name} | {c.StoreType} | {(c.IsNullable ? "是" : "否")} | {(c.IsPrimaryKey ? "✓" : "")} | {(c.IsForeignKey ? c.ForeignTable : "")} | {(c.IsIndexed ? "✓" : "")} | {c.DefaultValue} | {c.Comment} |");
                }
                sb.AppendLine();
            }
        }

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        return new RemoteStreamContent(stream, BuildFileName(domainGroup, "md"), "text/markdown");
    }

    private string BuildFileName(string? domainGroup, string ext)
        => $"数据库表字典{(domainGroup != null ? "_" + domainGroup : "")}_{Clock.Now:yyyyMMdd}.{ext}";

    private static bool Match(SchemaTableInfo t, string? filter, string? domainGroup)
    {
        if (domainGroup != null && t.DomainGroup != domainGroup) return false;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            return t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || t.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || t.Columns.Any(c =>
                    c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(c.Comment)
                        && c.Comment.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }
        return true;
    }

    private static DbTableDto ToDto(SchemaTableInfo t) => new()
    {
        Name = t.Name,
        Schema = t.Schema,
        DisplayName = t.DisplayName,
        DomainGroup = t.DomainGroup,
        Description = t.Description,
        Columns = t.Columns.Select(c => new DbColumnDto
        {
            Name = c.Name,
            StoreType = c.StoreType,
            ClrType = c.ClrType,
            MaxLength = c.MaxLength,
            IsNullable = c.IsNullable,
            IsPrimaryKey = c.IsPrimaryKey,
            IsForeignKey = c.IsForeignKey,
            ForeignTable = c.ForeignTable,
            IsIndexed = c.IsIndexed,
            DefaultValue = c.DefaultValue,
            Comment = c.Comment,
            IsSystemColumn = c.IsSystemColumn,
        }).ToList(),
    };
}
