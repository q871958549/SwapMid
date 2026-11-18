using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.SharedKernel.Employees;
using NSubstitute;
using Shouldly;
using Xunit;

namespace SwapMid.AccountManagement;

public class AccountUserEmployeeBindingResolverTests
{
    [Fact]
    public async Task FillBindingsAsync_Should_Use_Batched_Directory_Queries()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var enterpriseId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var organizationDirectory = Substitute.For<IOrganizationDirectory>();
        employeeDirectory
            .GetListByUserIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeSnapshot>
            {
                CreateSnapshot(Guid.NewGuid(), firstUserId, enterpriseId, departmentId, "E001", "甲"),
                CreateSnapshot(Guid.NewGuid(), secondUserId, enterpriseId, departmentId, "E002", "乙"),
            });
        organizationDirectory
            .GetEnterpriseNamesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [enterpriseId] = "SwapMid" });
        organizationDirectory
            .GetDepartmentListAsync(Arg.Any<CancellationToken>())
            .Returns(new List<DepartmentSnapshot>
            {
                new(departmentId, enterpriseId, null, "研发部", null),
            });
        var resolver = new AccountUserEmployeeBindingResolver(employeeDirectory, organizationDirectory);
        var rows = new List<AccountUserDto>
        {
            new() { Id = firstUserId },
            new() { Id = secondUserId },
        };

        await resolver.FillBindingsAsync(rows);

        rows.ShouldAllBe(row => row.BoundEnterpriseName == "SwapMid" && row.BoundDepartmentName == "研发部");
        await employeeDirectory.Received(1)
            .GetListByUserIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
        await organizationDirectory.Received(1)
            .GetEnterpriseNamesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
        await organizationDirectory.Received(1).GetDepartmentListAsync(Arg.Any<CancellationToken>());
        await organizationDirectory.DidNotReceive()
            .GetEnterpriseNameAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await organizationDirectory.DidNotReceive()
            .FindDepartmentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEmployeeOptionsAsync_And_GetDepartmentOptionsAsync_Should_Batch_Resolve_Labels()
    {
        var enterpriseId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var employeeDirectory = Substitute.For<IEmployeeDirectory>();
        var organizationDirectory = Substitute.For<IOrganizationDirectory>();

        employeeDirectory
            .GetActiveListAsync(null, Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeSnapshot>
            {
                new(
                    employeeId,
                    enterpriseId,
                    departmentId,
                    positionId,
                    null,
                    "SM001",
                    "张三",
                    "zhangsan@example.com",
                    "13800138000",
                    "兼容岗位",
                    EmployeeStatus.Active),
            });
        organizationDirectory
            .GetDepartmentListAsync(Arg.Any<CancellationToken>())
            .Returns(new List<DepartmentSnapshot>
            {
                new(departmentId, enterpriseId, null, "平台研发部", null),
            });
        organizationDirectory
            .GetEnterpriseNamesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [enterpriseId] = "SwapMid Group" });
        organizationDirectory
            .GetPositionNamesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [positionId] = "架构师" });

        var resolver = new AccountUserEmployeeBindingResolver(employeeDirectory, organizationDirectory);

        var employees = await resolver.GetEmployeeOptionsAsync();
        employees.Items.Count.ShouldBe(1);
        employees.Items[0].EnterpriseName.ShouldBe("SwapMid Group");
        employees.Items[0].DepartmentName.ShouldBe("平台研发部");
        employees.Items[0].Position.ShouldBe("架构师");

        var departments = await resolver.GetDepartmentOptionsAsync();
        departments.Items.Count.ShouldBe(1);
        departments.Items[0].Name.ShouldBe("平台研发部");
        departments.Items[0].EnterpriseName.ShouldBe("SwapMid Group");
    }

    private static EmployeeSnapshot CreateSnapshot(
        Guid employeeId,
        Guid userId,
        Guid enterpriseId,
        Guid departmentId,
        string employeeNo,
        string name)
        => new(
            employeeId,
            enterpriseId,
            departmentId,
            null,
            userId,
            employeeNo,
            name,
            null,
            null,
            null,
            EmployeeStatus.Active);
}
