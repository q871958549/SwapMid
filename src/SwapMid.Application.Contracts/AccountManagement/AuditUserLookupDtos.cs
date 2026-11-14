using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SwapMid.SharedKernel;

namespace SwapMid.AccountManagement;

/// <summary>按审计用户 Id 批量查询显示名的入参。</summary>
public class AuditUserLookupInput
{
    /// <summary>
    /// 待解析的用户 Id 集合。超过单请求上限时整批拒绝，调用方应按上限分片，不得依赖服务端截断。
    /// </summary>
    [Required]
    [MaxLength(BatchOperationLimits.MaxIdsPerRequest)]
    public List<Guid> Ids { get; set; } = new();
}

/// <summary>审计用户的最小展示信息（Id / 显示名 / 登录名；不含联系方式或员工档案）。</summary>
public class AuditUserLookupDto
{
    /// <summary>用户 Id。</summary>
    public Guid Id { get; set; }

    /// <summary>显示名；优先使用姓名，姓名为空时回退用户名。</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>登录用户名。面向全部登录用户的最小目录字段，不是联系方式或组织资料。</summary>
    public string UserName { get; set; } = null!;
}
