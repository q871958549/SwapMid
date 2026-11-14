using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.DeveloperDocs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace SwapMid.EntityFrameworkCore.DeveloperDocs;

/// <summary>
/// 反射 <see cref="SwapMidDbContext"/> 的 EF <c>IModel</c> + 实体 XML 注释，构建平台业务表的 Schema 元数据。
/// 结果在进程内静态缓存（IModel 编译期固定；Schema 变更随发版重启自动刷新）。
/// </summary>
public class EfCoreSchemaMetadataProvider : ISchemaMetadataProvider, ITransientDependency
{
    private const string PlatformNamespacePrefix = "SwapMid.";

    private static IReadOnlyList<SchemaTableInfo>? _cache;
    private static readonly SemaphoreSlim _buildLock = new(1, 1);

    private readonly IDbContextProvider<SwapMidDbContext> _dbContextProvider;
    private readonly XmlDocResolver _xmlDoc;
    private readonly ILogger<EfCoreSchemaMetadataProvider> _logger;

    public EfCoreSchemaMetadataProvider(
        IDbContextProvider<SwapMidDbContext> dbContextProvider,
        XmlDocResolver xmlDoc,
        ILogger<EfCoreSchemaMetadataProvider> logger)
    {
        _dbContextProvider = dbContextProvider;
        _xmlDoc = xmlDoc;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SchemaTableInfo>> GetTablesAsync()
    {
        if (_cache != null) return _cache;
        await _buildLock.WaitAsync();
        try
        {
            if (_cache != null) return _cache;
            _cache = await BuildAllAsync();
            return _cache;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private async Task<IReadOnlyList<SchemaTableInfo>> BuildAllAsync()
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var byName = new Dictionary<string, SchemaTableInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in Enumerate(dbContext.Model))
        {
            if (!byName.TryAdd(table.Name, table))
            {
                _logger.LogWarning(
                    "数据库表字典：表 {Table} 被多个实体重复映射，保留 {KeptType}，忽略 {IgnoredType}",
                    table.Name,
                    byName[table.Name].ClrTypeName,
                    table.ClrTypeName);
            }
        }

        return byName.Values
            .OrderBy(t => t.DomainGroup, StringComparer.Ordinal)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    private IEnumerable<SchemaTableInfo> Enumerate(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.IsOwned()) continue;

            var clrType = entityType.ClrType;
            if (clrType.FullName == null ||
                !clrType.FullName.StartsWith(PlatformNamespacePrefix, StringComparison.Ordinal))
                continue;

            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue;

            var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);

            var pkColumns = entityType.FindPrimaryKey()?.Properties.Select(p => p.Name).ToHashSet()
                            ?? new HashSet<string>();
            var fkColumns = new Dictionary<string, string?>();
            foreach (var fk in entityType.GetForeignKeys())
            {
                var principalTable = fk.PrincipalEntityType.GetTableName();
                foreach (var p in fk.Properties)
                    fkColumns[p.Name] = principalTable;
            }
            var indexedColumns = entityType.GetIndexes()
                .SelectMany(i => i.Properties.Select(p => p.Name))
                .ToHashSet();

            var typeSummary = _xmlDoc.GetTypeSummary(clrType);
            var table = new SchemaTableInfo
            {
                Name = tableName,
                Schema = entityType.GetSchema(),
                ClrTypeName = clrType.FullName,
                DisplayName = FirstSentence(typeSummary) ?? clrType.Name,
                Description = typeSummary,
                DomainGroup = ResolveDomainGroup(clrType),
            };

            foreach (var prop in entityType.GetProperties())
            {
                var columnName = storeObject.HasValue
                    ? prop.GetColumnName(storeObject.Value)
                    : prop.Name;
                var isSystem = AuditColumnNames.ContainsKey(prop.Name);
                table.Columns.Add(new SchemaColumnInfo
                {
                    Name = columnName ?? prop.Name,
                    StoreType = prop.GetColumnType() ?? string.Empty,
                    ClrType = prop.ClrType.Name,
                    MaxLength = prop.GetMaxLength(),
                    IsNullable = prop.IsNullable,
                    IsPrimaryKey = pkColumns.Contains(prop.Name),
                    IsForeignKey = fkColumns.ContainsKey(prop.Name),
                    ForeignTable = fkColumns.GetValueOrDefault(prop.Name),
                    IsIndexed = indexedColumns.Contains(prop.Name),
                    DefaultValue = prop.GetDefaultValueSql() ?? prop.GetDefaultValue()?.ToString(),
                    Comment = ResolveColumnComment(clrType, prop, isSystem),
                    IsSystemColumn = isSystem,
                });
            }

            yield return table;
        }
    }

    private static readonly Dictionary<string, string> AuditColumnNames = new(StringComparer.Ordinal)
    {
        ["Id"] = "主键",
        ["CreationTime"] = "创建时间",
        ["CreatorId"] = "创建人",
        ["LastModificationTime"] = "最后修改时间",
        ["LastModifierId"] = "最后修改人",
        ["IsDeleted"] = "是否删除",
        ["DeleterId"] = "删除人",
        ["DeletionTime"] = "删除时间",
        ["ConcurrencyStamp"] = "并发戳",
        ["ExtraProperties"] = "扩展属性",
        ["TenantId"] = "租户 Id",
    };

    private string? ResolveColumnComment(Type clrType, IProperty prop, bool isSystem)
    {
        if (prop.PropertyInfo != null)
        {
            var summary = _xmlDoc.GetPropertySummary(clrType, prop.Name);
            if (!string.IsNullOrEmpty(summary)) return summary;
        }
        return isSystem ? AuditColumnNames.GetValueOrDefault(prop.Name) : null;
    }

    private static string? FirstSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var idx = text.IndexOfAny(new[] { '。', '\n', '\r' });
        var first = idx > 0 ? text[..idx] : text;
        return first.Trim();
    }

    private static string ResolveDomainGroup(Type clrType)
    {
        var parts = (clrType.Namespace ?? string.Empty).Split('.');
        return parts.Length >= 2 ? parts[1] : "Other";
    }
}
