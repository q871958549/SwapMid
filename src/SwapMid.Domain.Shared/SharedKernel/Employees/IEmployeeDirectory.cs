using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.SharedKernel.Employees;

/// <summary>员工主数据只读目录。实现在企业管理域内。</summary>
public interface IEmployeeDirectory
{
    Task<EmployeeSnapshot?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<EmployeeSnapshot>> GetListAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<EmployeeSnapshot?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<EmployeeSnapshot>> GetListByUserIdsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default);
    Task<EmployeeSnapshot?> FindByEmployeeNoAsync(string employeeNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工号批量查询。去空白、大小写不敏感，并同时试有/无 HAN 前缀；
    /// 含软删除行；不按在职状态过滤。空集合返回空列表。
    /// </summary>
    Task<List<EmployeeSnapshot>> GetListByEmployeeNosAsync(
        IEnumerable<string> employeeNos,
        CancellationToken cancellationToken = default);

    Task<List<EmployeeSnapshot>> GetActiveListAsync(Guid? departmentId = null, CancellationToken cancellationToken = default);
    Task<List<EmployeeSnapshot>> GetAllListAsync(CancellationToken cancellationToken = default);

    /// <summary>按部门集合查询仍绑定账号的全部员工账号 Id；不按员工状态过滤，且必须在查询侧执行。</summary>
    Task<List<Guid>> GetBoundUserIdsByDepartmentIdsAsync(
        IEnumerable<Guid> departmentIds,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);
}
