using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp.Authorization;
using Xunit;

namespace SwapMid.DataIntegration;

public class DataIntegrationAuthorizationReflectionTests
{
    [Fact]
    public void Host门禁本体必须对任意tenant失败关闭并保留权限错误码()
    {
        const string permission = "DataIntegration.Guard.Contract";
        var exception = Should.Throw<AbpAuthorizationException>(() =>
            PipelineExecutionAppService.EnsureHostContext(Guid.NewGuid(), permission));
        exception.Code.ShouldBe(permission);

        Should.NotThrow(() =>
            PipelineExecutionAppService.EnsureHostContext(null, permission));
    }

    [Fact]
    public void 编译后类级和全部public入口必须只有契约声明的精确权限()
    {
        foreach (var contract in
                 DataIntegrationCrudAuthorizationContractTests.CreateContracts())
        {
            var serviceType = GetServiceType(contract.ClassName);
            serviceType.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
                .ShouldBeEmpty();
            serviceType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly)
                .Where(method => method.IsPrivate && method.IsFinal && method.IsVirtual)
                .ShouldBeEmpty("禁止显式接口实现绕过 public 入口闭集");
            GetPolicies(serviceType).ShouldBe(new string?[]
            {
                ResolveRuntimePermission(contract.ClassAuthorizePermission),
            });

            var methods = serviceType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public |
                            BindingFlags.DeclaredOnly)
                .ToDictionary(GetSignature);
            methods.Keys.OrderBy(x => x, StringComparer.Ordinal).ShouldBe(
                contract.Entries.Select(x => x.Signature)
                    .OrderBy(x => x, StringComparer.Ordinal));

            foreach (var entry in contract.Entries)
            {
                methods[entry.Signature]
                    .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
                    .ShouldBeEmpty();
                var expectedPolicies = entry.AuthorizePermission == null
                    ? Array.Empty<string?>()
                    : new string?[]
                    {
                        ResolveRuntimePermission(entry.AuthorizePermission),
                    };
                GetPolicies(methods[entry.Signature]).ShouldBe(expectedPolicies);
            }
        }
    }

    private static Type GetServiceType(string className)
        => className switch
        {
            nameof(DataSourceAppService) => typeof(DataSourceAppService),
            nameof(DataPipelineAppService) => typeof(DataPipelineAppService),
            nameof(PipelineExecutionAppService) => typeof(PipelineExecutionAppService),
            _ => throw new InvalidOperationException($"未登记运行时服务类型：{className}"),
        };

    private static string GetSignature(MethodInfo method)
        => method.Name + "(" +
           string.Join(",", method.GetParameters()
               .Select(parameter => parameter.ParameterType.Name)) + ")";

    private static string?[] GetPolicies(MemberInfo member)
        => member.GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .Select(attribute => attribute.Policy)
            .ToArray();

    private static string ResolveRuntimePermission(string sourcePermission)
        => sourcePermission switch
        {
            "SwapMidPermissions.DataIntegration.DataSources.Default" =>
                Permissions.SwapMidPermissions.DataIntegration.DataSources.Default,
            "SwapMidPermissions.DataIntegration.DataSources.Create" =>
                Permissions.SwapMidPermissions.DataIntegration.DataSources.Create,
            "SwapMidPermissions.DataIntegration.DataSources.Update" =>
                Permissions.SwapMidPermissions.DataIntegration.DataSources.Update,
            "SwapMidPermissions.DataIntegration.DataSources.Delete" =>
                Permissions.SwapMidPermissions.DataIntegration.DataSources.Delete,
            "SwapMidPermissions.DataIntegration.DataPipelines.Default" =>
                Permissions.SwapMidPermissions.DataIntegration.DataPipelines.Default,
            "SwapMidPermissions.DataIntegration.DataPipelines.Create" =>
                Permissions.SwapMidPermissions.DataIntegration.DataPipelines.Create,
            "SwapMidPermissions.DataIntegration.DataPipelines.Update" =>
                Permissions.SwapMidPermissions.DataIntegration.DataPipelines.Update,
            "SwapMidPermissions.DataIntegration.DataPipelines.Execute" =>
                Permissions.SwapMidPermissions.DataIntegration.DataPipelines.Execute,
            "SwapMidPermissions.DataIntegration.DataPipelines.Delete" =>
                Permissions.SwapMidPermissions.DataIntegration.DataPipelines.Delete,
            "SwapMidPermissions.DataIntegration.PipelineExecutions.Default" =>
                Permissions.SwapMidPermissions.DataIntegration.PipelineExecutions.Default,
            "SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome" =>
                Permissions.SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome,
            "SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning" =>
                Permissions.SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning,
            "SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt" =>
                Permissions.SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt,
            _ => throw new InvalidOperationException($"未登记运行时权限：{sourcePermission}"),
        };
}
