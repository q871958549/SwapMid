using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SwapMid.SharedKernel;
using SwapMid.SharedKernel.Employees;

namespace SwapMid.EnterpriseManagement;

/// <summary>批量启用/停用入参（部门、岗位）。</summary>
public class BatchSetEnabledInput
{
    /// <summary>上限同 <see cref="BatchOperationLimits.MaxIdsPerRequest"/>（超限整批 400 拒绝，不截断）。</summary>
    [Required]
    [MaxLength(BatchOperationLimits.MaxIdsPerRequest)]
    public List<Guid> Ids { get; set; } = new();

    public bool IsEnabled { get; set; }
}

/// <summary>批量设置员工状态入参（在职/停职/离职）。</summary>
public class BatchSetEmployeeStatusInput
{
    /// <summary>上限同 <see cref="BatchOperationLimits.MaxIdsPerRequest"/>（超限整批 400 拒绝，不截断）。</summary>
    [Required]
    [MaxLength(BatchOperationLimits.MaxIdsPerRequest)]
    public List<Guid> Ids { get; set; } = new();

    public EmployeeStatus Status { get; set; }
}

/// <summary>勾选员工批量开通账号入参（员工管理页勾选行「批量生成账号」）。
/// 专属 DTO 而非复用 SwapMid.EnterpriseManagement.BatchIdsInput：RoleNames 是本端点特有字段，
/// 不能污染全仓约 30 处 BatchDelete 端点共用的通用批量入参。</summary>
/// <remarks>上文旧全名是为保持既有 OpenAPI 描述与 schemaId；真实 CLR 类型为
/// <see cref="BatchIdsInput"/>（<c>SwapMid.SharedKernel</c>）。</remarks>
public class BatchGenerateAccountsInput
{
    /// <summary>员工 Id 列表（语义同 SwapMid.EnterpriseManagement.BatchIdsInput.Ids）。
    /// 上限同 <see cref="BatchOperationLimits.MaxIdsPerRequest"/>（超限整批 400 拒绝，不截断）。
    /// 要覆盖全员规模请改用 <see cref="BulkGenerateAccountsInput"/>——那条按条件在服务端查询，
    /// 不受任何 id 列表长度限制，正是「批量上限」这类约束该留的正门。</summary>
    /// <remarks>旧全名指向兼容保留的 OpenAPI schemaId；CLR 属性语义对应
    /// <see cref="BatchIdsInput.Ids"/>。</remarks>
    [Required]
    [MaxLength(BatchOperationLimits.MaxIdsPerRequest)]
    public List<Guid> Ids { get; set; } = new();

    /// <summary>批量开通时追加授予的角色名（可选；null/空=沿用现行为，仅授予默认角色）。
    /// 语义与 <see cref="BulkGenerateAccountsInput.RoleNames"/> 完全一致：
    /// 追加于默认角色之上（并集，不替换），重复项幂等去重；含不存在的角色名整批前置拒绝（零账号创建）。
    /// 非空时要求调用者另持账号域「分配角色」权限（AccountManagement.Users.Update），且系统内置角色（isStatic，如 admin）不可授予。</summary>
    [MaxLength(20)]
    public List<string>? RoleNames { get; set; }
}

/// <summary>按范围一键为"在职且未开通账号"的员工批量开通——服务端自行查询全部符合条件者，不受前端分页(每页 N 条)限制。</summary>
public class BulkGenerateAccountsInput
{
    /// <summary>限定企业（可空=全部企业）。</summary>
    public Guid? EnterpriseId { get; set; }

    /// <summary>限定部门（可空=不限部门）。</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>批量开通时追加授予的角色名（可选；null/空=沿用现行为，仅授予默认角色）。
    /// 语义为在默认角色基础上"追加"（并集，不替换），与默认角色重复的项自动幂等去重。
    /// 全部角色名须已存在，否则整批前置拒绝（不创建任何账号），不进入逐条容错。
    /// 非空时要求调用者另持账号域「分配角色」权限（AccountManagement.Users.Update），且系统内置角色（isStatic，如 admin）不可授予。</summary>
    [MaxLength(20)]
    public List<string>? RoleNames { get; set; }
}

/// <summary>批量生成账号结果。</summary>
public class BatchAccountResultDto : BatchOperationResultDto
{
    /// <summary>新建账号数。</summary>
    public int Created { get; set; }

    /// <summary>已绑定（已有账号）数。</summary>
    public int AlreadyBound { get; set; }
}
