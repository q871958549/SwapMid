using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwapMid.EnterpriseManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Xunit;

namespace SwapMid.Domain.Tests.EnterpriseManagement;

public class EnterpriseDemoDataSeedSafetyTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public async Task 默认不访问组织数据(string environmentName)
    {
        var (seed, enterprises) = Create(environmentName, null);
        await seed.SeedAsync(new DataSeedContext());
        await enterprises.DidNotReceive().GetQueryableAsync();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task 非开发环境误开启也必须拒绝且不访问数据库(string environmentName)
    {
        var (seed, enterprises) = Create(environmentName, "true");
        await Should.ThrowAsync<InvalidOperationException>(() => seed.SeedAsync(new DataSeedContext()));
        await enterprises.DidNotReceive().GetQueryableAsync();
    }

    private static (EnterpriseManagementTestDataSeedContributor Seed, IRepository<Enterprise, Guid> Enterprises) Create(string environmentName, string? enabled)
    {
        var enterprises = Substitute.For<IRepository<Enterprise, Guid>>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EnterpriseManagement:EnableDemoData"] = enabled,
        }).Build();
        return (new EnterpriseManagementTestDataSeedContributor(enterprises,
            Substitute.For<IRepository<CompanyDepartment, Guid>>(), Substitute.For<IRepository<Employee, Guid>>(),
            Substitute.For<IRepository<Position, Guid>>(), Substitute.For<IGuidGenerator>(),
            Substitute.For<IAsyncQueryableExecuter>(), configuration, environment), enterprises);
    }
}
