using SwapMid.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace SwapMid.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(SwapMidEntityFrameworkCoreModule),
    typeof(SwapMidApplicationContractsModule)
)]
public class SwapMidDbMigratorModule : AbpModule
{
}
