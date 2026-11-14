using System.ComponentModel.DataAnnotations;
using Volo.Abp.Auditing;

namespace SwapMid.DataIntegration;

/// <summary>结果待核对执行的人工确认输入。</summary>
public sealed class ResolvePipelineExecutionOutcomeInput
{
    /// <summary>目标库核验结论。</summary>
    [Required]
    public PipelineExecutionResolution? Resolution { get; set; }

    /// <summary>确认已应用时可补录处理行数。</summary>
    [Range(0, long.MaxValue)]
    public long? ProcessedRows { get; set; }

    /// <summary>
    /// 新客户端应提交结构化核验证据引用：incident:/change:/work-order:/dba-evidence:
    /// 前缀后接 8–128 位不透明编号。为兼容已发布客户端，HTTP 契约仍接收旧 8–512
    /// 字符说明；服务端不会保存旧说明正文，而会替换为不可逆的安全占位引用。
    /// 不得粘贴 URL、SQL、连接串、令牌或密钥。
    /// </summary>
    [Required]
    [StringLength(512, MinimumLength = 8)]
    [DisableAuditing]
    public string VerificationNote { get; set; } = string.Empty;
}
