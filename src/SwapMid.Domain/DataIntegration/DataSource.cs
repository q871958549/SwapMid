using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>数据源（ETL 数据管道的读取来源或写入目标，如数据库 / REST API / 文件）。</summary>
public class DataSource : FullAuditedAggregateRoot<Guid>
{
    /// <summary>数据源名称。</summary>
    public string Name { get; private set; } = null!;

    /// <summary>数据源描述（可空）。</summary>
    public string? Description { get; private set; }

    /// <summary>数据源类型（关系型数据库 / REST API / 文件）。</summary>
    public DataSourceType Type { get; private set; }

    /// <summary>连接配置（JSON，含连接串 / 地址等，按类型解析）。</summary>
    [DisableAuditing]
    public string Config { get; private set; } = null!;

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; private set; }

    private DataSource() { }

    public DataSource(Guid id, string name, DataSourceType type, string config, string? description = null)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), DataSourceConsts.MaxNameLength);
        Type = type;
        Config = Check.NotNullOrWhiteSpace(config, nameof(config), DataSourceConsts.MaxConfigLength);
        Description = description;
        IsEnabled = true;
    }

    public void Update(string name, DataSourceType type, string config, string? description)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), DataSourceConsts.MaxNameLength);
        Type = type;
        Config = Check.NotNullOrWhiteSpace(config, nameof(config), DataSourceConsts.MaxConfigLength);
        Description = description;
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
}
