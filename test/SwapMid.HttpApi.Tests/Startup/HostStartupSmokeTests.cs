using SwapMid.EntityFrameworkCore;
using SwapMid.Monitoring;
using SwapMid.OpenIddict;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using Shouldly;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;

namespace SwapMid.HttpApi.Tests.Startup;

/// <summary>
/// 启动冒烟：真实构建 <see cref="SwapMidHttpApiHostModule"/> 的 ABP/Autofac 容器。
///
/// 🔴 为什么需要这道门：本仓库已两次发生「编译通过 + 单元测试通过 + CI 全绿，生产启动即死」。
///   · 2026-07-29：<c>RunScheduledJobHangfireJob</c> 加了 <c>sealed</c>，而它带 <c>[UnitOfWork]</c>，
///     ABP 要为其生成 Castle 类代理（派生自本类）——sealed 无法被继承，
///     <c>TypeLoadException</c> 抛在容器构建阶段，api 容器 Restarting 循环、全站接口不可用。
///   · 同类风险还包括：约定注册脱靶、拦截器冲突、模块依赖环、PreConfigureServices 期的 fail-fast 守卫。
///
/// 既有测试为何拦不住：<c>ContractTestHost</c> 自搭管线、**不加载 Host 模块**；其余引用该模块的
/// 测试（FileLengthGuard / ProductionReadinessConfiguration 等）都只做文本与配置断言，不构建容器。
///
/// 本测试刻意只走到 <c>builder.Build()</c>，**不调用 <c>InitializeApplicationAsync</c>**——
/// 上述故障全部发生在容器构建阶段，而连 PostgreSQL / Redis / RabbitMQ / Hangfire 发生在其之后。
/// 因此这道门无需任何外部系统，可直接跟随 <c>dotnet test SwapMid.slnx</c> 运行。
/// </summary>
public class HostStartupSmokeTests
{
    [Fact]
    [Trait("Category", "HostStartupSmoke")]
    public async Task Host_module_container_builds_without_error()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Development：跳过 PreConfigureServices 里针对非开发环境的生产密钥强度守卫
            // （CI 上没有 .env / User Secrets，用 appsettings.json 的占位值会被它按设计拒绝启动）。
            EnvironmentName = Environments.Development,
            // ContentRoot 必须指向 Host 项目：模块要读 appsettings.json，
            // 并以它为基准解析简历/样品附件落盘根做隔离校验。
            ContentRootPath = ResolveHostProjectRoot(),
        });

        // 与 Program.cs 保持同一装配顺序；UseAutofac 是关键——sealed 类代理失败正是在
        // Autofac 的 AutofacRegistration.Populate 阶段抛出的。
        builder.Host.UseAutofac();

        await builder.AddApplicationAsync<SwapMidHttpApiHostModule>();

        // 断言点就是这一行不抛异常：容器能构建 = 全部服务注册、拦截器与代理生成成功。
        await using var app = builder.Build();

        app.Services.ShouldNotBeNull();

        // 🔴 容器构建过关**不等于** DbContext 配置过关（2026-08-03 第三次同类事故）：
        // `AbpDbContextOptions` 里登记的是**惰性**委托，要等 DbContextOptions<T>（Transient 工厂）
        // 首次被 resolve 才执行。当时误用 Configure（单值槽，覆盖）而非 PreConfigure（列表，累加）
        // 注册 EF 拦截器，把 UseNpgsql 整个顶掉——编译 0 警告、877 个后端测试全绿，
        // 而生产一起进程就反复抛 No database provider。
        // 下面这一步把这道门往前推：真 resolve 一次 options，强制跑完整条 Pre/Configure 动作链。
        using var scope = app.Services.CreateScope();
        // 主 DbContext：走 default Configure 槽，正是当时被顶掉的那类。
        AssertDbContextOptionsShape<SwapMidDbContext>(scope.ServiceProvider);
        // ABP 内置 DbContext：当时的第一现场（后台作业轮询最先触发，日志里刷的就是它）。
        AssertDbContextOptionsShape<BackgroundJobsDbContext>(scope.ServiceProvider);

        // 自定义 OpenIddict Extension Grant 必须在 ServerBuilder (AllowCustomFlow) 与
        // AbpOpenIddictExtensionGrantsOptions.Grants 两处成对登记，否则 /connect/token 返回 ID2032。
        var oidcServerOptions = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        oidcServerOptions.GrantTypes.ShouldContain(ImpersonationTokenExtensionGrant.ExtensionGrantName);
        oidcServerOptions.GrantTypes.ShouldContain(ExternalOidcTokenExtensionGrant.ExtensionGrantName);

        var extensionGrants = scope.ServiceProvider.GetRequiredService<IOptions<AbpOpenIddictExtensionGrantsOptions>>().Value;
        extensionGrants.Grants.Keys.ShouldContain(ImpersonationTokenExtensionGrant.ExtensionGrantName);
        extensionGrants.Grants.Keys.ShouldContain(ExternalOidcTokenExtensionGrant.ExtensionGrantName);
    }

    /// <summary>
    /// 断言某个 DbContext 的 options 形状：provider 在、观测拦截器也在。
    ///
    /// 两条断言缺一不可——只断 provider 会漏掉「拦截器没挂上」（观测静默失效，指标恒为空而无人察觉）；
    /// 只断拦截器会漏掉本次事故（provider 被顶掉）。
    /// 这里不连数据库：UseNpgsql 只记录连接串、不开连接。
    /// </summary>
    private static void AssertDbContextOptionsShape<TDbContext>(IServiceProvider serviceProvider)
        where TDbContext : DbContext
    {
        // resolve 即触发 DbContextOptionsFactory.Create，执行全部 Pre/Configure 动作。
        var options = serviceProvider.GetRequiredService<DbContextOptions<TDbContext>>();

        options.Extensions.OfType<RelationalOptionsExtension>().ShouldNotBeEmpty(
            $"{typeof(TDbContext).Name} 没有关系型 provider 配置——多半是有人用 " +
            "AbpDbContextOptions.Configure(...)（单值槽，会覆盖 UseNpgsql）做了追加型定制，" +
            "追加请改用 PreConfigure(...)");

        options.FindExtension<CoreOptionsExtension>()!.Interceptors
            .ShouldNotBeNull()
            .ShouldContain(interceptor => interceptor is PlatformEfCommandMetricsInterceptor,
                $"{typeof(TDbContext).Name} 未挂上 EF 命令观测拦截器——慢查询指标会静默失效");
    }

    private static string ResolveHostProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "SwapMid.slnx")))
        {
            directory = directory.Parent;
        }

        var repositoryRoot = directory?.FullName
            ?? throw new DirectoryNotFoundException("无法定位仓库根目录");
        var hostRoot = Path.Combine(
            repositoryRoot, "src", "SwapMid.HttpApi.Host");
        Directory.Exists(hostRoot).ShouldBeTrue($"未找到 Host 项目目录：{hostRoot}");
        return hostRoot;
    }
}
