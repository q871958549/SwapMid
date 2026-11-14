using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SwapMid.Data;
using Volo.Abp.DependencyInjection;

namespace SwapMid.EntityFrameworkCore;

public class EntityFrameworkCoreSwapMidDbSchemaMigrator
    : ISwapMidDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreSwapMidDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the SwapMidDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<SwapMidDbContext>()
            .Database
            .MigrateAsync();
    }
}
