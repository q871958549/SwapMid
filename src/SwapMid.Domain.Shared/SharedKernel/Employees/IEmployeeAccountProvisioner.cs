using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwapMid.SharedKernel.Employees;

/// <summary>员工开户与禁号契约。签名只接受员工 Id，禁止跨域泄漏 Employee 实体。</summary>
public interface IEmployeeAccountProvisioner
{
    Task<EmployeeAccountProvisionResult> ProvisionAsync(
        Guid employeeId,
        IReadOnlyCollection<string>? roleNames = null);

    Task<bool> DisableAccountAsync(Guid employeeId);
}

/// <summary>员工账号开通结果的内部不可变契约。</summary>
public sealed record EmployeeAccountProvisionResult(
    Guid EmployeeId,
    Guid? UserId,
    string? UserName,
    bool Created,
    bool AlreadyBound,
    string Message);
