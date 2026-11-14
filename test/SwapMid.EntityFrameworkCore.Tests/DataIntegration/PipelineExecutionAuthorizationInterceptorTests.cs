using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using SwapMid.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// 执行裁决、受控恢复与目标回执核验的真实 ABP 动态代理授权测试。恢复真实授权服务与
/// 方法拦截器，仅把权限来源替换为 AsyncLocal 可控集合，证明类级 Default 与精确方法级
/// 子权限按 AND 生效，而不是只验证特性是否存在。
/// </summary>
[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
[Collection(PipelineExecutionAuthorizationInterceptorTestCollection.Name)]
public class PipelineExecutionAuthorizationInterceptorTests
    : SwapMidTestBase<
        PipelineExecutionAuthorizationInterceptorTestModule>
{
    private const string DefaultPermission =
        SwapMidPermissions.DataIntegration
            .PipelineExecutions.Default;

    private readonly IPipelineExecutionAppService _service;
    private readonly PipelineExecutionTestPermissionChecker
        _permissionChecker;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public PipelineExecutionAuthorizationInterceptorTests()
    {
        _service = GetRequiredService<IPipelineExecutionAppService>();
        _permissionChecker =
            (PipelineExecutionTestPermissionChecker)
            GetRequiredService<IPermissionChecker>();
        _currentUser = GetRequiredService<ICurrentUser>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task 高风险入口全部重载必须同时持有Default与精确子权限才能进入方法体()
    {
        _currentUser.IsAuthenticated.ShouldBeTrue();
        _currentUser.Id.ShouldNotBeNull();
        _currentTenant.Id.ShouldBeNull();

        await AssertDefaultAndExactChildAsync(
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.ResolveOutcome,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.RecoverRunning,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionResolutionInvalid,
            () => _service.ResolveOutcomeAsync(
                Guid.Empty,
                InvalidResolutionInput()),
            () => _service.ResolveOutcomeAsync(
                Guid.Empty,
                InvalidResolutionInput(),
                CancellationToken.None));

        await AssertDefaultAndExactChildAsync(
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.RecoverRunning,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.VerifyTargetReceipt,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionRecoveryInvalid,
            () => _service.RecoverRunningAsync(
                Guid.Empty,
                ValidRecoveryInput()),
            () => _service.RecoverRunningAsync(
                Guid.Empty,
                ValidRecoveryInput(),
                CancellationToken.None));

        await AssertDefaultAndExactChildAsync(
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.VerifyTargetReceipt,
            SwapMidPermissions.DataIntegration
                .PipelineExecutions.ResolveOutcome,
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionReceiptInconclusive,
            () => _service.VerifyTargetReceiptAsync(Guid.Empty),
            () => _service.VerifyTargetReceiptAsync(
                Guid.Empty,
                CancellationToken.None));
    }

    private async Task AssertDefaultAndExactChildAsync(
        string childPermission,
        string wrongSiblingPermission,
        string expectedBodyErrorCode,
        params Func<Task>[] calls)
    {
        foreach (var call in calls)
        {
            await AssertRejectedByProxyAsync(call);
            await AssertRejectedByProxyAsync(call, DefaultPermission);
            await AssertRejectedByProxyAsync(call, childPermission);
            await AssertRejectedByProxyAsync(
                call,
                DefaultPermission,
                wrongSiblingPermission);

            using (_permissionChecker.Grant(
                       DefaultPermission,
                       childPermission))
            {
                var bodyException =
                    await Should.ThrowAsync<BusinessException>(call);
                bodyException.Code.ShouldBe(expectedBodyErrorCode);
            }
        }
    }

    private async Task AssertRejectedByProxyAsync(
        Func<Task> call,
        params string[] permissions)
    {
        using (_permissionChecker.Grant(permissions))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(call);
        }
    }

    private static ResolvePipelineExecutionOutcomeInput
        InvalidResolutionInput()
        => new()
        {
            Resolution = (PipelineExecutionResolution)int.MaxValue,
            VerificationNote = "incident:AUTHORIZATION-TEST-0001",
        };

    private static RecoverPipelineExecutionInput ValidRecoveryInput()
    {
        var now = DateTimeOffset.UtcNow;
        return new RecoverPipelineExecutionInput
        {
            RecoveryRequestId = Guid.NewGuid(),
            ExpectedConcurrencyStamp = "auth-test-stamp",
            DrainStartedAt = now.AddMinutes(-8),
            OldOwnerExitVerifiedAt = now.AddMinutes(-7),
            TargetSessionExitVerifiedAt = now.AddMinutes(-6),
            EvidenceReference = "incident:AUTHORIZATION-TEST-0001",
        };
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PipelineExecutionAuthorizationInterceptorTestCollection
{
    public const string Name =
        "Pipeline execution real authorization interceptor tests";
}

[DependsOn(typeof(SwapMidEntityFrameworkCoreTestModule))]
public class PipelineExecutionAuthorizationInterceptorTestModule : AbpModule
{
    public override void ConfigureServices(
        ServiceConfigurationContext context)
    {
        context.Services.Replace(
            ServiceDescriptor.Transient<
                IAuthorizationService,
                AbpAuthorizationService>());
        context.Services.Replace(
            ServiceDescriptor.Transient<
                IAbpAuthorizationService,
                AbpAuthorizationService>());
        context.Services.Replace(
            ServiceDescriptor.Transient<
                IMethodInvocationAuthorizationService,
                MethodInvocationAuthorizationService>());
        context.Services.Replace(
            ServiceDescriptor.Singleton<
                IPermissionChecker,
                PipelineExecutionTestPermissionChecker>());
        context.Services.Replace(
            ServiceDescriptor.Singleton<
                ICurrentPrincipalAccessor,
                PipelineExecutionAuthenticatedPrincipalAccessor>());
    }
}

public sealed class PipelineExecutionTestPermissionChecker :
    IPermissionChecker
{
    private static readonly AsyncLocal<HashSet<string>?>
        GrantedPermissions = new();

    public IDisposable Grant(params string[] permissions)
    {
        var previous = GrantedPermissions.Value;
        GrantedPermissions.Value = new HashSet<string>(
            permissions,
            StringComparer.Ordinal);
        return new RestorePermissions(previous);
    }

    public Task<bool> IsGrantedAsync(string name)
        => Task.FromResult(
            GrantedPermissions.Value?.Contains(name) == true);

    public Task<bool> IsGrantedAsync(
        ClaimsPrincipal? claimsPrincipal,
        string name)
        => IsGrantedAsync(name);

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names)
        => IsGrantedAsync(null, names);

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(
        ClaimsPrincipal? claimsPrincipal,
        string[] names)
    {
        var result = new MultiplePermissionGrantResult();
        foreach (var name in names)
        {
            result.Result[name] =
                GrantedPermissions.Value?.Contains(name) == true
                    ? PermissionGrantResult.Granted
                    : PermissionGrantResult.Prohibited;
        }

        return Task.FromResult(result);
    }

    private sealed class RestorePermissions(
        HashSet<string>? previous) : IDisposable
    {
        public void Dispose()
        {
            GrantedPermissions.Value = previous;
        }
    }
}

public sealed class PipelineExecutionAuthenticatedPrincipalAccessor :
    ThreadCurrentPrincipalAccessor
{
    protected override ClaimsPrincipal GetClaimsPrincipal()
        => new(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(
                        AbpClaimTypes.UserId,
                        "56ca9c68-b45b-4504-8a71-a27994a21a5c"),
                    new Claim(
                        AbpClaimTypes.UserName,
                        "pipeline-execution-auth-test"),
                },
                authenticationType:
                "PipelineExecutionAuthorizationTest"));
}
