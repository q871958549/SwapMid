namespace SwapMid.SystemManagement;

/// <summary>
/// 编码规则「可选业务对象」——由各业务域向后端注册，作为前端下拉选项与列表中文名映射的**单一事实源**
/// （替代此前前端硬编码闭集，杜绝新域一扩展就漏配、显英文/空下拉）。
/// </summary>
public class CodeRuleEntityTypeDto
{
    /// <summary>业务对象标识（= <see cref="CodeRuleDto.EntityType"/>，各域常量，须逐字一致）。</summary>
    public string EntityType { get; set; } = null!;
    /// <summary>业务对象中文名（如「项目」「样品」，作下拉标签与列表显示名）。</summary>
    public string DisplayName { get; set; } = null!;
    /// <summary>所属业务域分组（下拉可按此分组，如「企业管理」「项目管理」）。</summary>
    public string Group { get; set; } = null!;
}
