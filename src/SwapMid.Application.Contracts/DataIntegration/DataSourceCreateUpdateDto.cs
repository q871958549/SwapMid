using System.ComponentModel.DataAnnotations;
using Volo.Abp.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>数据源新增/编辑入参</summary>
public class DataSourceCreateUpdateDto
{
    /// <summary>数据源名称</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = null!;

    /// <summary>数据源描述</summary>
    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>数据源类型</summary>
    [Required]
    [EnumDataType(typeof(DataSourceType))]
    public DataSourceType Type { get; set; }

    /// <summary>
    /// 完整连接配置 JSON。关系型更新仅在连接目标与 TLS 姿态不变时允许省略 password；
    /// REST API/文件类型的存量配置不可回显，更新时必须重新提交完整非空对象。
    /// </summary>
    [Required]
    [MaxLength(2048)]
    [DisableAuditing]
    public string Config { get; set; } = null!;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
