using System.Linq;
using System.Reflection;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace SwapMid.DataIntegration;

public class PipelineExecutionAuthorizationContractTests
{
    [Fact]
    public void DataIntegration全部权限必须仅允许Host侧授权()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new PermissionDefinitionContext(services);
        new SwapMidPermissionDefinitionProvider().Define(context);

        var permissions = new[]
        {
            SwapMidPermissions.DataIntegration.DataSources.Default,
            SwapMidPermissions.DataIntegration.DataSources.Create,
            SwapMidPermissions.DataIntegration.DataSources.Update,
            SwapMidPermissions.DataIntegration.DataSources.Delete,
            SwapMidPermissions.DataIntegration.DataSources.Default +
                SwapMidPermissions.ExportSuffix,
            SwapMidPermissions.DataIntegration.DataSources.Default +
                SwapMidPermissions.ImportSuffix,
            SwapMidPermissions.DataIntegration.DataPipelines.Default,
            SwapMidPermissions.DataIntegration.DataPipelines.Create,
            SwapMidPermissions.DataIntegration.DataPipelines.Update,
            SwapMidPermissions.DataIntegration.DataPipelines.Delete,
            SwapMidPermissions.DataIntegration.DataPipelines.Execute,
            SwapMidPermissions.DataIntegration.DataPipelines.Default +
                SwapMidPermissions.ExportSuffix,
            SwapMidPermissions.DataIntegration.DataPipelines.Default +
                SwapMidPermissions.ImportSuffix,
            SwapMidPermissions.DataIntegration.PipelineExecutions.Default,
            SwapMidPermissions.DataIntegration.PipelineExecutions.ResolveOutcome,
            SwapMidPermissions.DataIntegration.PipelineExecutions.RecoverRunning,
            SwapMidPermissions.DataIntegration.PipelineExecutions.VerifyTargetReceipt,
            SwapMidPermissions.DataIntegration.PipelineExecutions.Default +
                SwapMidPermissions.ExportSuffix,
            SwapMidPermissions.DataIntegration.PipelineExecutions.Default +
                SwapMidPermissions.ImportSuffix,
            SwapMidPermissions.DataIntegration.CdcStreams.Default,
            SwapMidPermissions.DataIntegration.CdcStreams.Create,
            SwapMidPermissions.DataIntegration.CdcStreams.Update,
            SwapMidPermissions.DataIntegration.CdcStreams.Delete,
            SwapMidPermissions.DataIntegration.CdcStreams.Manage,
            SwapMidPermissions.DataIntegration.CdcStreams.Default +
                SwapMidPermissions.ExportSuffix,
            SwapMidPermissions.DataIntegration.CdcStreams.Default +
                SwapMidPermissions.ImportSuffix,
        };

        var definitions = context
            .GetGroup(
                SwapMidPermissions.GroupName +
                ".DataIntegration")
            .GetPermissionsWithChildren();
        definitions
            .Select(definition => definition.Name)
            .OrderBy(name => name)
            .ShouldBe(
                permissions.OrderBy(name => name),
                "新增 DataIntegration 权限时必须显式纳入 Host-only 契约");
        foreach (var definition in definitions)
        {
            definition.MultiTenancySide.ShouldBe(
                MultiTenancySides.Host,
                $"{definition.Name} 操作的是非 IMultiTenant 的宿主级数据");
        }
    }

    [Fact]
    public void ResolveOutcome全部重载必须使用独立裁决权限()
    {
        SwapMidPermissions.DataIntegration.PipelineExecutions
            .ResolveOutcome
            .ShouldBe(
                SwapMidPermissions.DataIntegration
                    .PipelineExecutions.Default + ".ResolveOutcome");

        var methods = typeof(PipelineExecutionAppService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method =>
                method.Name == nameof(
                    PipelineExecutionAppService.ResolveOutcomeAsync))
            .ToArray();

        methods.Length.ShouldBe(2, "新增或删除重载时必须同步复核全部授权入口");
        foreach (var method in methods)
        {
            var policies = method
                .GetCustomAttributes<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy)
                .ToArray();
            policies.ShouldBe(new[]
            {
                SwapMidPermissions.DataIntegration
                    .PipelineExecutions.ResolveOutcome,
            });
        }
    }

    [Fact]
    public void RecoverRunning全部重载必须使用独立恢复权限()
    {
        SwapMidPermissions.DataIntegration.PipelineExecutions
            .RecoverRunning
            .ShouldBe(
                SwapMidPermissions.DataIntegration
                    .PipelineExecutions.Default + ".RecoverRunning");

        var methods = typeof(PipelineExecutionAppService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method =>
                method.Name == nameof(
                    PipelineExecutionAppService.RecoverRunningAsync))
            .ToArray();

        methods.Length.ShouldBe(2, "新增或删除重载时必须同步复核全部授权入口");
        foreach (var method in methods)
        {
            var policies = method
                .GetCustomAttributes<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy)
                .ToArray();
            policies.ShouldBe(new[]
            {
                SwapMidPermissions.DataIntegration
                    .PipelineExecutions.RecoverRunning,
            });
        }
    }

    [Fact]
    public void VerifyTargetReceipt全部重载必须使用独立机器核验权限()
    {
        SwapMidPermissions.DataIntegration.PipelineExecutions
            .VerifyTargetReceipt
            .ShouldBe(
                SwapMidPermissions.DataIntegration
                    .PipelineExecutions.Default + ".VerifyTargetReceipt");

        var methods = typeof(PipelineExecutionAppService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method =>
                method.Name == nameof(
                    PipelineExecutionAppService.VerifyTargetReceiptAsync))
            .ToArray();

        methods.Length.ShouldBe(2,
            "新增或删除重载时必须同步复核全部授权与事务边界");
        foreach (var method in methods)
        {
            var policies = method
                .GetCustomAttributes<AuthorizeAttribute>()
                .Select(attribute => attribute.Policy)
                .ToArray();
            policies.ShouldBe(new[]
            {
                SwapMidPermissions.DataIntegration
                    .PipelineExecutions.VerifyTargetReceipt,
            });
        }
    }
}
