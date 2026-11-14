using System.ComponentModel.DataAnnotations;
using Volo.Abp.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>测试数据源连通性的入参：直接传当前编辑中的类型 + 配置 JSON，无需先保存。</summary>
public class DataSourceTestConnectionInput
{
    /// <summary>数据源类型</summary>
    [Required]
    [EnumDataType(typeof(DataSourceType))]
    public DataSourceType Type { get; set; }

    /// <summary>
    /// 待测试的完整连接配置 JSON；按已保存数据源测试时，只有连接目标与 TLS 姿态不变
    /// 才允许省略关系型 password。
    /// </summary>
    [Required]
    [MaxLength(2048)]
    [DisableAuditing]
    public string Config { get; set; } = null!;
}

/// <summary>数据源连通性测试结果</summary>
public class TestConnectionResultDto
{
    /// <summary>是否连接成功</summary>
    public bool Success { get; set; }
    /// <summary>结果消息(失败时为错误原因)</summary>
    public string Message { get; set; } = null!;
    /// <summary>耗时(毫秒)</summary>
    public long ElapsedMs { get; set; }
}
