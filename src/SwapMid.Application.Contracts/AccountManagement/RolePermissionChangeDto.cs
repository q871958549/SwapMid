using System;
using System.Collections.Generic;

namespace SwapMid.AccountManagement;

/// <summary>角色权限变更审计记录（从审计日志 <c>AuditLog.Comments</c> 按固定前缀提取的语义化 diff）。</summary>
public class RolePermissionChangeDto
{
    /// <summary>变更发生时间（对应本次 PUT 请求的执行时间）。</summary>
    public DateTime ExecutionTime { get; set; }
    /// <summary>操作人用户名（未登录/系统调用可能为空）。</summary>
    public string? UserName { get; set; }
    /// <summary>变更摘要行（形如 <c>{roleName} +[a,b] -[c]</c>，已剥离固定前缀 <c>[RolePermissionChange] </c>、
    /// 行首保留角色名；通常一条 PUT 请求对应一行;若该次请求未产生实际权限变化，为空列表。
    /// 消费端按 <c>+[...]</c>/<c>-[...]</c> 括号锚点解析,勿依赖行首形态）。</summary>
    public List<string> Changes { get; set; } = new();
}
