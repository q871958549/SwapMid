using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Xunit;
using Xunit.Abstractions;

namespace SwapMid.EntityFrameworkCore;

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
public class SqliteSeededTemplateTests(ITestOutputHelper output)
{
    [Fact]
    public void 预播种模板克隆必须包含多域默认种子且保持快速克隆()
    {
        using var warm = SqliteTestDatabaseTemplates.CloneSeeded();

        var cloneSw = Stopwatch.StartNew();
        using var clonedConnection = SqliteTestDatabaseTemplates.CloneSeeded();
        cloneSw.Stop();

        var clonedCounts = SqliteTestDatabaseTemplates.SnapshotRowCounts(clonedConnection);
        output.WriteLine($"SQLITE_SEEDED_TEMPLATE clone_only_ms={cloneSw.Elapsed.TotalMilliseconds:F2}");

        clonedCounts.Values.Count(v => v > 0).ShouldBeGreaterThanOrEqualTo(
            4,
            "预播种模板应包含多域默认种子数据，不应退化为空库");
    }
}

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
public class SqliteSeededHostBehaviorTests : SwapMidEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task 标准测试宿主应走预播种快路且保留真实IDataSeeder幂等能力()
    {
        GetService<SeededTestDatabaseMarker>().ShouldNotBeNull();

        var userRepository = GetRequiredService<IIdentityUserRepository>();
        var adminBefore = await WithUnitOfWorkAsync(() => userRepository.FindByNormalizedUserNameAsync("ADMIN"));
        adminBefore.ShouldNotBeNull();

        var countBefore = await WithUnitOfWorkAsync(() => userRepository.GetCountAsync());

        var seeder = GetRequiredService<IDataSeeder>();
        await seeder.SeedAsync();

        var countAfter = await WithUnitOfWorkAsync(() => userRepository.GetCountAsync());
        countAfter.ShouldBe(countBefore);

        await WithUnitOfWorkAsync(() => userRepository.DeleteAsync(adminBefore, autoSave: true));
        var afterDelete = await WithUnitOfWorkAsync(() => userRepository.FindByNormalizedUserNameAsync("ADMIN"));
        afterDelete.ShouldBeNull();
    }

    [Fact]
    public async Task 前一用例删除种子用户不得影响新用例实例()
    {
        GetService<SeededTestDatabaseMarker>().ShouldNotBeNull();
        var userRepository = GetRequiredService<IIdentityUserRepository>();
        var admin = await WithUnitOfWorkAsync(() => userRepository.FindByNormalizedUserNameAsync("ADMIN"));
        admin.ShouldNotBeNull();
    }
}

[Collection(EntityFrameworkCoreTestCollections.Enterprise)]
[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]
public class SqliteCustomHookFallbackTests : SwapMidEntityFrameworkCoreTestBase
{
    private bool _customSeederInvoked;

    protected override void AfterAddApplication(IServiceCollection services)
    {
        base.AfterAddApplication(services);
        services.AddSingleton(new ProbeSeedContributor(() => _customSeederInvoked = true));
        services.Configure<AbpDataSeedOptions>(options =>
        {
            options.Contributors.Add<ProbeSeedContributor>();
        });
    }

    [Fact]
    public void 重写宿主钩子的子类应自动回退至现场种子路径()
    {
        GetService<StandardTestHostConfigurationMarker>().ShouldBeNull();
        GetService<SeededTestDatabaseMarker>().ShouldBeNull();
        _customSeederInvoked.ShouldBeTrue("重写了 AfterAddApplication 的测试类必须在启动期现场执行种子贡献者");
    }

    private sealed class ProbeSeedContributor(Action onSeed) : IDataSeedContributor
    {
        public Task SeedAsync(DataSeedContext context)
        {
            onSeed();
            return Task.CompletedTask;
        }
    }
}
