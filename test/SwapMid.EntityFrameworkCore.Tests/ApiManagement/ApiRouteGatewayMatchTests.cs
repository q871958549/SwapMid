using System;
using System.Threading.Tasks;
using SwapMid.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace SwapMid.ApiManagement;

/// <summary>
/// 网关路由匹配(<see cref="IApiRouteRepository.FindEnabledMatchAsync"/>)测试——匹配错就转发错,守护核心查询:
/// 精确方法 / ALL 通配 / 具体优先 ALL / 忽略大小写 / 启用过滤 / Order 优先 / 无匹配返回 null。
/// </summary>
[Collection(EntityFrameworkCoreTestCollections.Platform)]
public class ApiRouteGatewayMatchTests : SwapMidEntityFrameworkCoreTestBase
{
    private Task SeedAsync(string path, HttpMethodType method, bool enabled = true, int order = 0,
        string upstream = "https://upstream/x")
        => WithUnitOfWorkAsync(async () =>
        {
            var repo = GetRequiredService<IApiRouteRepository>();
            var route = new ApiRoute(Guid.NewGuid(), $"r-{Guid.NewGuid():N}", path, method, upstream, order: order);
            if (!enabled)
            {
                route.Disable();
            }
            await repo.InsertAsync(route, autoSave: true);
        });

    private Task<ApiRoute?> MatchAsync(string path, HttpMethodType method)
        => WithUnitOfWorkAsync(() =>
            GetRequiredService<IApiRouteRepository>().FindEnabledMatchAsync(path, method));

    [Fact]
    public async Task 精确方法命中_其他方法不命中()
    {
        var p = $"/gw-{Guid.NewGuid():N}";
        await SeedAsync(p, HttpMethodType.GET);

        (await MatchAsync(p, HttpMethodType.GET)).ShouldNotBeNull();
        (await MatchAsync(p, HttpMethodType.POST)).ShouldBeNull();   // 无 POST、无 ALL → 不命中
    }

    [Fact]
    public async Task ALL通配命中任意方法()
    {
        var p = $"/gw-{Guid.NewGuid():N}";
        await SeedAsync(p, HttpMethodType.ALL);

        (await MatchAsync(p, HttpMethodType.GET)).ShouldNotBeNull();
        (await MatchAsync(p, HttpMethodType.DELETE)).ShouldNotBeNull();
    }

    [Fact]
    public async Task 具体方法优先于ALL通配()
    {
        var p = $"/gw-{Guid.NewGuid():N}";
        await SeedAsync(p, HttpMethodType.ALL, upstream: "https://upstream/all");
        await SeedAsync(p, HttpMethodType.GET, upstream: "https://upstream/get");

        var m = await MatchAsync(p, HttpMethodType.GET);
        m!.UpstreamUrl.ShouldBe("https://upstream/get");
    }

    [Fact]
    public async Task path忽略大小写()
    {
        var p = $"/Gw-{Guid.NewGuid():N}";   // 含大写
        await SeedAsync(p, HttpMethodType.GET);

        (await MatchAsync(p.ToLowerInvariant(), HttpMethodType.GET)).ShouldNotBeNull();
    }

    [Fact]
    public async Task 禁用路由不命中()
    {
        var p = $"/gw-{Guid.NewGuid():N}";
        await SeedAsync(p, HttpMethodType.GET, enabled: false);

        (await MatchAsync(p, HttpMethodType.GET)).ShouldBeNull();
    }

    [Fact]
    public async Task 多条命中取Order小的()
    {
        // 两条都命中 GET 请求(GET 本身 + ALL 通配),method 不同以避开 (Path,Method) 唯一索引。
        // Order 是第一排序键:order=1 击败 order=5(即便 order=5 那条是更"具体"的 GET)。
        var p = $"/gw-{Guid.NewGuid():N}";
        await SeedAsync(p, HttpMethodType.GET, order: 5, upstream: "https://upstream/order5");
        await SeedAsync(p, HttpMethodType.ALL, order: 1, upstream: "https://upstream/order1");

        var m = await MatchAsync(p, HttpMethodType.GET);
        m!.UpstreamUrl.ShouldBe("https://upstream/order1");
    }

    [Fact]
    public async Task 无匹配返回null()
    {
        (await MatchAsync($"/nope-{Guid.NewGuid():N}", HttpMethodType.GET)).ShouldBeNull();
    }
}
