using Microsoft.AspNetCore.Identity;
using Volo.Abp.Modularity;

namespace SwapMid;

[DependsOn(
    typeof(SwapMidDomainModule),
    typeof(SwapMidTestBaseModule)
)]
public class SwapMidDomainTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<PasswordHasherOptions>(options =>
        {
            // 测试宿主无需跑生产默认 100,000 次 PBKDF2-SHA512，降至 1,000 次消除海量建用户/种子验密纯 CPU 空转。
            options.IterationCount = 1000;
        });
    }
}
