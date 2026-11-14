using System;
using System.Linq;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using SwapMid.SharedKernel.Employees;
using Shouldly;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.EnterpriseManagement;

/// <summary>
/// 回归 2026-09-10：后台作业 Handler 无 ambient UoW 时 GetActiveListAsync 释放 DbContext。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class EmployeeDirectoryTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IEmployeeDirectory _directory;
    private readonly IEmployeeAppService _employees;
    private readonly IEnterpriseAppService _enterprises;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public EmployeeDirectoryTests()
    {
        _directory = GetRequiredService<IEmployeeDirectory>();
        _employees = GetRequiredService<IEmployeeAppService>();
        _enterprises = GetRequiredService<IEnterpriseAppService>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    [Fact]
    public async Task GetActiveListAsync_无ambient_UoW时仍能读到在职员工()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var employeeNo = $"T{suffix}";
        var enterpriseId = await EnsureEnterpriseAsync();
        await _employees.CreateAsync(new EmployeeCreateUpdateDto
        {
            EnterpriseId = enterpriseId,
            EmployeeNo = employeeNo,
            Name = "对账预加载",
            Email = $"{employeeNo}@example.test",
            Status = EmployeeStatus.Active,
        });

        _unitOfWorkManager.Current.ShouldBeNull();

        var actives = await _directory.GetActiveListAsync();
        actives.ShouldContain(item => item.EmployeeNo == employeeNo);

        var all = await _directory.GetAllListAsync();
        all.ShouldContain(item => item.EmployeeNo == employeeNo);
        all.Single(item => item.EmployeeNo == employeeNo).Name.ShouldBe("对账预加载");
    }

    private async Task<Guid> EnsureEnterpriseAsync()
    {
        var existing = await _enterprises.GetListAsync(new GetEnterpriseListInput { MaxResultCount = 1 });
        if (existing.Items.Count > 0)
        {
            return existing.Items[0].Id;
        }

        var created = await _enterprises.CreateAsync(new EnterpriseCreateUpdateDto
        {
            Code = $"ENT{Guid.NewGuid():N}"[..12],
            Name = "员工目录测试企业",
        });
        await _enterprises.SetAsMainAsync(created.Id);
        return created.Id;
    }
}
