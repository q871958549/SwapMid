using System;
using System.Reflection;
using System.Threading.Tasks;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace SwapMid.EnterpriseManagement;

/// <summary>
/// override Create/Update 会丢掉 CrudAppService 的 Check*PolicyAsync，
/// 构造函数里的 *PolicyName 随之失效。必须方法级 [Authorize(Create/Update)]（CompInfo 同款）。
/// </summary>
public class EnterpriseMasterDataWriteAuthorizationTests
{
    [Theory]
    [InlineData(typeof(EmployeeAppService), "CreateAsync", SwapMidPermissions.EnterpriseManagement.Employees.Create)]
    [InlineData(typeof(EmployeeAppService), "UpdateAsync", SwapMidPermissions.EnterpriseManagement.Employees.Update)]
    [InlineData(typeof(EnterpriseAppService), "CreateAsync", SwapMidPermissions.EnterpriseManagement.Enterprises.Create)]
    [InlineData(typeof(EnterpriseAppService), "UpdateAsync", SwapMidPermissions.EnterpriseManagement.Enterprises.Update)]
    [InlineData(typeof(CompanyDepartmentAppService), "CreateAsync", SwapMidPermissions.EnterpriseManagement.Departments.Create)]
    [InlineData(typeof(CompanyDepartmentAppService), "UpdateAsync", SwapMidPermissions.EnterpriseManagement.Departments.Update)]
    [InlineData(typeof(PositionAppService), "CreateAsync", SwapMidPermissions.EnterpriseManagement.Positions.Create)]
    [InlineData(typeof(PositionAppService), "UpdateAsync", SwapMidPermissions.EnterpriseManagement.Positions.Update)]
    public void Override_Create_Update_Must_Have_Method_Level_Authorize(Type service, string methodName, string policy)
    {
        var method = service.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        method.ShouldNotBeNull($"{service.Name}.{methodName} 必须 override 且带方法级 Authorize");
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        authorize.ShouldNotBeNull($"{service.Name}.{methodName} 缺少方法级 [Authorize]");
        authorize!.Policy.ShouldBe(policy);
        method.ReturnType.IsGenericType.ShouldBeTrue();
        method.ReturnType.GetGenericTypeDefinition().ShouldBe(typeof(Task<>));
    }
}
