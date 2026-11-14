using System;
using Volo.Abp.Application.Dtos;

namespace SwapMid.DataIntegration;

/// <summary>数据源</summary>
public class DataSourceDto : FullAuditedEntityDto<Guid>
{
    /// <summary>数据源名称</summary>
    public string Name { get; set; } = null!;
    /// <summary>数据源描述</summary>
    public string? Description { get; set; }
    /// <summary>数据源类型</summary>
    public DataSourceType Type { get; set; }

    /// <summary>
    /// 可公开回显的配置 JSON。关系型只包含
    /// provider/host/port/database/username/targetResourceGroup/encrypt/trustServerCertificate 白名单字段；
    /// REST API 与文件类型固定返回空对象，绝不透传存储原文。
    /// </summary>
    public string Config { get; set; } = null!;

    /// <summary>是否存在未回显的密码、连接串、未知非空字段或不透明配置</summary>
    public bool HasSecret { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }
}
