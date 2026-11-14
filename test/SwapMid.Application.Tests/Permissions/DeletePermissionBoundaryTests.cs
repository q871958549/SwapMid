using System;
using System.Linq;
using System.Reflection;
using SwapMid.ApiManagement;
using SwapMid.DataIntegration;
using SwapMid.EnterpriseManagement;
using SwapMid.MessageBus;
using SwapMid.SystemManagement;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace SwapMid.Permissions;

public class DeletePermissionBoundaryTests
{
    [Theory]
    [InlineData(typeof(DataSourceAppService), "DeleteAsync", SwapMidPermissions.DataIntegration.DataSources.Delete)]
    [InlineData(typeof(DataPipelineAppService), "DeleteAsync", SwapMidPermissions.DataIntegration.DataPipelines.Delete)]
    [InlineData(typeof(ApiGroupAppService), "DeleteAsync", SwapMidPermissions.ApiManagement.ApiGroups.Delete)]
    [InlineData(typeof(ApiRouteAppService), "DeleteAsync", SwapMidPermissions.ApiManagement.ApiRoutes.Delete)]
    [InlineData(typeof(MessageTopicAppService), "DeleteAsync", SwapMidPermissions.MessageBus.Topics.Delete)]
    [InlineData(typeof(MessageSubscriptionAppService), "DeleteAsync", SwapMidPermissions.MessageBus.Subscriptions.Delete)]
    [InlineData(typeof(EnterpriseAppService), "DeleteAsync", SwapMidPermissions.EnterpriseManagement.Enterprises.Delete)]
    [InlineData(typeof(CompanyDepartmentAppService), "BatchDeleteAsync", SwapMidPermissions.EnterpriseManagement.Departments.Delete)]
    [InlineData(typeof(PositionAppService), "BatchDeleteAsync", SwapMidPermissions.EnterpriseManagement.Positions.Delete)]
    [InlineData(typeof(EmployeeAppService), "BatchDeleteAsync", SwapMidPermissions.EnterpriseManagement.Employees.Delete)]
    [InlineData(typeof(CodeRuleAppService), "DeleteAsync", SwapMidPermissions.SystemManagement.CodeRules.Delete)]
    [InlineData(typeof(DataDictionaryAppService), "DeleteAsync", SwapMidPermissions.SystemManagement.DataDictionaries.Delete)]
    public void 删除与批量删除端点必须挂独立Delete权限(Type serviceType, string methodName, string expectedPolicy)
    {
        var method = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == methodName);
        var authorize = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        authorize.ShouldNotBeEmpty($"{serviceType.Name}.{methodName} 缺少 [Authorize]");
        authorize.Select(a => a.Policy).ShouldContain(expectedPolicy);
    }

    [Fact]
    public void PermissionDefinitionProvider源码中不得存在孤立的Delete常量未注册()
    {
        var defined = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        CollectConstStrings(typeof(SwapMidPermissions), defined);

        var deleteConsts = defined
            .Where(p => p.EndsWith(".Delete", StringComparison.Ordinal))
            .ToList();

        deleteConsts.Count.ShouldBeGreaterThanOrEqualTo(10);
    }

    private static void CollectConstStrings(Type type, System.Collections.Generic.HashSet<string> sink)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            {
                if (field.GetRawConstantValue() is string val)
                {
                    sink.Add(val);
                }
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
        {
            CollectConstStrings(nested, sink);
        }
    }
}
