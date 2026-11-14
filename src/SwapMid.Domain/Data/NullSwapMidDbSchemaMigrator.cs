using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Data;

/* This is used if database provider does't define
 * ISwapMidDbSchemaMigrator implementation.
 */
public class NullSwapMidDbSchemaMigrator : ISwapMidDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
