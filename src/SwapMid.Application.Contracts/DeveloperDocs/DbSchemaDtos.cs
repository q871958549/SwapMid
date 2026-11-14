using System.Collections.Generic;

namespace SwapMid.DeveloperDocs;

/// <summary>数据库表字典——表摘要（列表项）。</summary>
public class DbTableSummaryDto
{
    /// <summary>表名（如 AppDataDictionaries）。</summary>
    public string Name { get; set; } = null!;

    /// <summary>中文显示名。</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>所属业务域。</summary>
    public string DomainGroup { get; set; } = null!;

    /// <summary>字段数。</summary>
    public int ColumnCount { get; set; }

    /// <summary>表说明。</summary>
    public string? Description { get; set; }
}

/// <summary>数据库表字典——表详情（含字段明细）。</summary>
public class DbTableDto
{
    /// <summary>表名。</summary>
    public string Name { get; set; } = null!;

    /// <summary>schema 名。</summary>
    public string? Schema { get; set; }

    /// <summary>中文显示名。</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>所属业务域。</summary>
    public string DomainGroup { get; set; } = null!;

    /// <summary>表说明。</summary>
    public string? Description { get; set; }

    /// <summary>字段列表。</summary>
    public List<DbColumnDto> Columns { get; set; } = new();
}

/// <summary>数据库表字典——字段。</summary>
public class DbColumnDto
{
    /// <summary>列名。</summary>
    public string Name { get; set; } = null!;

    /// <summary>数据库存储类型（如 character varying(64)）。</summary>
    public string StoreType { get; set; } = null!;

    /// <summary>CLR 类型短名。</summary>
    public string ClrType { get; set; } = null!;

    /// <summary>最大长度（字符串列）。</summary>
    public int? MaxLength { get; set; }

    /// <summary>是否可空。</summary>
    public bool IsNullable { get; set; }

    /// <summary>是否主键。</summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>是否外键。</summary>
    public bool IsForeignKey { get; set; }

    /// <summary>外键关联表名。</summary>
    public string? ForeignTable { get; set; }

    /// <summary>是否建有索引。</summary>
    public bool IsIndexed { get; set; }

    /// <summary>默认值。</summary>
    public string? DefaultValue { get; set; }

    /// <summary>中文含义（取实体属性 XML 注释，空 = 待补）。</summary>
    public string? Comment { get; set; }

    /// <summary>是否系统/审计字段（ABP 基类字段，前端可折叠置底）。</summary>
    public bool IsSystemColumn { get; set; }
}

/// <summary>外部数据源（表字典阶段二：ERP/WMS/CRM 等平台外部业务库）。</summary>
public class ExternalDbSourceDto
{
    /// <summary>连接串名（appsettings ConnectionStrings 的 key，如 OrderReadDb）。</summary>
    public string Key { get; set; } = null!;

    /// <summary>中文显示名（如「订单中心从库」）。</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>方言（SqlServer / PostgreSql），决定元数据查询语法。</summary>
    public string Dialect { get; set; } = null!;
}
