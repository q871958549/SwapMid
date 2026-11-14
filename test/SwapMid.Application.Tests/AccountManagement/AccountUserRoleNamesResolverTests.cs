using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Xunit;

namespace SwapMid.AccountManagement;

public class AccountUserRoleNamesResolverTests
{
    [Fact]
    public async Task GetRoleNamesByUserIdsAsync_Should_Not_Query_When_Empty()
    {
        var userRepository = Substitute.For<IRepository<IdentityUser, Guid>>();
        var roleRepository = Substitute.For<IIdentityRoleRepository>();
        var asyncExecuter = Substitute.For<IAsyncQueryableExecuter>();
        var resolver = new AccountUserRoleNamesResolver(userRepository, roleRepository, asyncExecuter);

        var result = await resolver.GetRoleNamesByUserIdsAsync(Array.Empty<Guid>());

        result.ShouldBeEmpty();
        await userRepository.DidNotReceive().GetQueryableAsync();
        await roleRepository.DidNotReceive().GetListAsync(cancellationToken: Arg.Any<CancellationToken>());
    }
}
