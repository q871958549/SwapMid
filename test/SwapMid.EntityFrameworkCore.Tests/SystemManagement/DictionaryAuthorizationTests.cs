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
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace SwapMid.SystemManagement;

[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
public class DictionaryAuthorizationTests : SwapMidTestBase<DictionaryAuthorizationTestModule>
{
    [Fact]
    public async Task ReadOnlyCannotMutate_AndWriteRequiresBothDefaultAndActionPermission()
    {
        var service = GetRequiredService<IDataDictionaryAppService>();
        var checker = (DictionaryTestPermissionChecker)GetRequiredService<IPermissionChecker>();
        const string read = SwapMidPermissions.SystemManagement.DataDictionaries.Default;
        const string create = SwapMidPermissions.SystemManagement.DataDictionaries.Create;
        const string update = SwapMidPermissions.SystemManagement.DataDictionaries.Update;
        const string delete = SwapMidPermissions.SystemManagement.DataDictionaries.Delete;

        Guid dictionaryId;
        DataDictionaryItemDto item;
        var dictCode = $"auth_dict_{Guid.NewGuid():N}";

        using (checker.Grant(read, create, update))
        {
            var dictionary = await service.CreateAsync(new() { Code = dictCode, Name = "权限测试字典", IsEnabled = true });
            dictionaryId = dictionary.Id;
            item = await service.CreateItemAsync(new()
            {
                DictionaryId = dictionaryId,
                Label = "权限测试项",
                Value = Guid.NewGuid().ToString("N"),
                IsEnabled = true,
            });
        }

        using (checker.Grant(read))
        {
            (await service.GetAsync(dictionaryId)).Code.ShouldBe(dictCode);
            (await service.GetItemsAsync(dictionaryId)).Items.Count.ShouldBe(1);

            await Should.ThrowAsync<AbpAuthorizationException>(() => service.CreateAsync(
                new() { Code = dictCode + "_2", Name = "越权新建", IsEnabled = true }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.UpdateAsync(dictionaryId,
                new() { Code = dictCode, Name = "越权改名", IsEnabled = true }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.DeleteAsync(dictionaryId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.UpdateItemAsync(item.Id,
                new() { DictionaryId = dictionaryId, Label = "越权", Value = item.Value, IsEnabled = true }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.CreateItemAsync(
                new() { DictionaryId = dictionaryId, Label = "越权", Value = "denied", IsEnabled = true }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.DeleteItemAsync(item.Id));
        }

        using (checker.Grant(update))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.UpdateItemAsync(item.Id,
                new() { DictionaryId = dictionaryId, Label = "无读取权限", Value = item.Value, IsEnabled = true }));
        }

        using (checker.Grant(read, delete))
        {
            await service.DeleteAsync(dictionaryId);
        }
    }
}

[DependsOn(typeof(SwapMidEntityFrameworkCoreTestModule))]
public class DictionaryAuthorizationTestModule : AbpModule
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
                DictionaryTestPermissionChecker>());
        context.Services.Replace(
            ServiceDescriptor.Singleton<
                ICurrentPrincipalAccessor,
                DictionaryAuthenticatedPrincipalAccessor>());
    }
}

public sealed class DictionaryTestPermissionChecker :
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

public sealed class DictionaryAuthenticatedPrincipalAccessor :
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
                        "dictionary-auth-test"),
                },
                authenticationType:
                "DictionaryAuthorizationTest"));
}
