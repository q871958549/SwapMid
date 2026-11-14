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
