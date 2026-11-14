using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using SwapMid.Permissions;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace SwapMid.AccountManagement;

/// <summary>
/// 模拟登录五条否决的会失败测试（不变量 #5）。规则只在 <see cref="ImpersonationGuardService"/>；
/// 前端按钮与 BFF action 不是边界。本文件受 CODEOWNERS 约束。
/// </summary>
public class ImpersonationGuardTests
{
    private static readonly string Impersonate =
        SwapMidPermissions.AccountManagement.Users.Impersonate;

    [Fact]
    public async Task 链式模拟必须拒绝且不得再查权限()
    {
        var permission = Substitute.For<IPermissionChecker>();
        var users = new FakeUsers();
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var principal = Principal(callerId, impersonatorId: Guid.NewGuid());

        var result = await new ImpersonationGuardService(permission, users)
            .ValidateAsync(principal, targetId);

        result.IsAllowed.ShouldBeFalse();
        result.DenyReason.ShouldBe(ImpersonationDenyReason.AlreadyImpersonating);
        await permission.DidNotReceive()
            .IsGrantedAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>());
        users.FindCalls.ShouldBe(0);
    }

    [Fact]
    public async Task 缺少调用者身份必须拒绝()
    {
        var permission = Substitute.For<IPermissionChecker>();
        var users = new FakeUsers();

        var result = await new ImpersonationGuardService(permission, users)
            .ValidateAsync(new ClaimsPrincipal(new ClaimsIdentity()), Guid.NewGuid());

        result.IsAllowed.ShouldBeFalse();
        result.DenyReason.ShouldBe(ImpersonationDenyReason.InvalidCaller);
        await permission.DidNotReceive()
            .IsGrantedAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>());
    }

    [Fact]
    public async Task 无模拟登录权限必须拒绝()
    {
        var permission = Substitute.For<IPermissionChecker>();
        var users = new FakeUsers();
        var callerId = Guid.NewGuid();
        permission.IsGrantedAsync(Arg.Any<ClaimsPrincipal>(), Impersonate).Returns(false);

        var result = await new ImpersonationGuardService(permission, users)
            .ValidateAsync(Principal(callerId), Guid.NewGuid());

        result.IsAllowed.ShouldBeFalse();
        result.DenyReason.ShouldBe(ImpersonationDenyReason.PermissionDenied);
        users.FindCalls.ShouldBe(0);
    }

    [Fact]
    public async Task 模拟自己必须拒绝()
    {
        var callerId = Guid.NewGuid();
        var (guard, users) = Guard(granted: true);

        var result = await guard.ValidateAsync(Principal(callerId), callerId);

        result.IsAllowed.ShouldBeFalse();
        result.DenyReason.ShouldBe(ImpersonationDenyReason.SelfImpersonation);
        users.FindCalls.ShouldBe(0);
    }

    [Fact]
    public async Task 目标不存在与停用与非管理员模拟管理员必须同属TargetUnavailable()
    {
        var callerId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var caller = User(callerId, "hr");
        var inactive = User(inactiveId, "gone");
        inactive.SetIsActive(false);
        var admin = User(adminId, "boss");

        var (guard, users) = Guard(granted: true);
        users.Add(caller);
        users.Add(inactive);
        users.Add(admin, isAdmin: true);

        var missing = await guard.ValidateAsync(Principal(callerId), missingId);
        var stopped = await guard.ValidateAsync(Principal(callerId), inactiveId);
        var escalate = await guard.ValidateAsync(Principal(callerId), adminId);

        missing.DenyReason.ShouldBe(ImpersonationDenyReason.TargetUnavailable);
        stopped.DenyReason.ShouldBe(ImpersonationDenyReason.TargetUnavailable);
        escalate.DenyReason.ShouldBe(ImpersonationDenyReason.TargetUnavailable);
        missing.IsAllowed.ShouldBeFalse();
        stopped.IsAllowed.ShouldBeFalse();
        escalate.IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task 管理员模拟普通账号必须放行并带回双方用户()
    {
        var callerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var caller = User(callerId, "admin");
        var target = User(targetId, "staff");
        var (guard, users) = Guard(granted: true);
        users.Add(caller, isAdmin: true);
        users.Add(target);

        var result = await guard.ValidateAsync(Principal(callerId), targetId);

        result.IsAllowed.ShouldBeTrue();
        result.DenyReason.ShouldBe(ImpersonationDenyReason.None);
        result.Caller.ShouldBe(caller);
        result.Target.ShouldBe(target);
    }

    private static (ImpersonationGuardService Guard, FakeUsers Users) Guard(bool granted)
    {
        var permission = Substitute.For<IPermissionChecker>();
        var users = new FakeUsers();
        permission.IsGrantedAsync(Arg.Any<ClaimsPrincipal>(), Impersonate).Returns(granted);
        return (new ImpersonationGuardService(permission, users), users);
    }

    private sealed class FakeUsers : IImpersonationIdentityLookup
    {
        private readonly Dictionary<Guid, IdentityUser> _users = new();
        private readonly HashSet<Guid> _admins = new();

        public int FindCalls { get; private set; }

        public void Add(IdentityUser user, bool isAdmin = false)
        {
            _users[user.Id] = user;
            if (isAdmin)
            {
                _admins.Add(user.Id);
            }
        }

        public Task<IdentityUser?> FindAsync(Guid id)
        {
            FindCalls++;
            return Task.FromResult(_users.TryGetValue(id, out var user) ? user : null);
        }

        public Task<bool> IsInRoleAsync(IdentityUser user, string roleName)
            => Task.FromResult(roleName == "admin" && _admins.Contains(user.Id));
    }

    private static ClaimsPrincipal Principal(Guid userId, Guid? impersonatorId = null)
    {
        var claims = new[] { new Claim(AbpClaimTypes.UserId, userId.ToString()) };
        if (impersonatorId.HasValue)
        {
            claims =
            [
                claims[0],
                new Claim(AbpClaimTypes.ImpersonatorUserId, impersonatorId.Value.ToString()),
            ];
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "ImpersonationGuardTest"));
    }

    private static IdentityUser User(Guid id, string userName)
        => new(id, userName, $"{userName}@example.test");
}
