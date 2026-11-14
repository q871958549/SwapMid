using System.Collections.Generic;

namespace SwapMid.DeveloperDocs;

/// <summary>
/// 数据库表的反射元数据（来自 EF <c>IModel</c> + 实体 XML 注释，只读、不落库）。
/// 供「数据库表字典」模块呈现与导出。
/// </summary>
public class SchemaTableInfo
{
    /// <summary>数据库表名（如 <c>AppDataDictionaries</c>）。</summary>
    public string Name { get; set; } = null!;

    /// <summary>schema 名（PostgreSQL 默认 public，可空）。</summary>
    public string? Schema { get; set; }

    /// <summary>对应实体 CLR 全名（如 <c>SwapMid.SystemManagement.DataDictionary</c>）。</summary>
    public string ClrTypeName { get; set; } = null!;

    /// <summary>中文显示名（取实体 XML summary，缺失时回退实体短名）。</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>所属业务域（取实体 namespace 第二段，如 SystemManagement）。</summary>
    public string DomainGroup { get; set; } = null!;

    /// <summary>表说明（实体 XML summary，可空）。</summary>
    public string? Description { get; set; }

    /// <summary>字段列表。</summary>
    public List<SchemaColumnInfo> Columns { get; set; } = new();
}

/// <summary>数据库列的反射元数据。</summary>
public class SchemaColumnInfo
{
    /// <summary>列名。</summary>
    public string Name { get; set; } = null!;

    /// <summary>数据库存储类型（如 <c>character varying(64)</c>）。</summary>
    public string StoreType { get; set; } = null!;

    /// <summary>CLR 类型短名（如 <c>String</c>、<c>Guid</c>）。</summary>
    public string ClrType { get; set; } = null!;

    /// <summary>最大长度（字符串列），无则为 null。</summary>
    public int? MaxLength { get; set; }

    /// <summary>是否可空。</summary>
    public bool IsNullable { get; set; }

    /// <summary>是否主键列。</summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>是否外键列。</summary>
    public bool IsForeignKey { get; set; }

    /// <summary>外键关联的表名（IsForeignKey 时有值）。</summary>
    public string? ForeignTable { get; set; }

    /// <summary>是否建有索引。</summary>
    public bool IsIndexed { get; set; }

    /// <summary>默认值（SQL 字面或常量），无则为 null。</summary>
    public string? DefaultValue { get; set; }

    /// <summary>中文含义（取实体属性 XML summary，可空 = 待补注释）。</summary>
    public string? Comment { get; set; }

    /// <summary>是否系统/审计字段（ABP 基类字段，如 Id/CreationTime/IsDeleted；中文名由内置映射兜底，前端可折叠）。</summary>
    public bool IsSystemColumn { get; set; }
}
