using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.Testing;
using Volo.Abp.Uow;

namespace SwapMid;

public abstract class SwapMidTestBase<TStartupModule> : AbpIntegratedTest<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly ConcurrentDictionary<Type, bool> UsesUnmodifiedHostHooksCache = new();

    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }

    protected override void BeforeAddApplication(IServiceCollection services)
    {
        services.ReplaceConfiguration(TestConfigurationSnapshotCache.Build());

        if (UsesUnmodifiedHostHooks(GetType()))
        {
            services.AddSingleton(StandardTestHostConfigurationMarker.Instance);
        }
    }

    private static bool UsesUnmodifiedHostHooks(Type testType)
    {
        return UsesUnmodifiedHostHooksCache.GetOrAdd(testType, static type =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var baseType = typeof(SwapMidTestBase<TStartupModule>);
            var abpBaseType = typeof(AbpIntegratedTest<TStartupModule>);

            var setOptions = type.GetMethod(nameof(SetAbpApplicationCreationOptions), flags);
            var beforeAdd = type.GetMethod(nameof(BeforeAddApplication), flags);
            var afterAdd = type.GetMethod(nameof(AfterAddApplication), flags);

            return setOptions?.DeclaringType == baseType
                && beforeAdd?.DeclaringType == baseType
                && afterAdd?.DeclaringType == abpBaseType;
        });
    }

    protected virtual Task WithUnitOfWorkAsync(Func<Task> func)
    {
        return WithUnitOfWorkAsync(new AbpUnitOfWorkOptions(), func);
    }

    protected virtual async Task WithUnitOfWorkAsync(AbpUnitOfWorkOptions options, Func<Task> action)
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            using (var uow = uowManager.Begin(options))
            {
                await action();

                await uow.CompleteAsync();
            }
        }
    }

    protected virtual Task<TResult> WithUnitOfWorkAsync<TResult>(Func<Task<TResult>> func)
    {
        return WithUnitOfWorkAsync(new AbpUnitOfWorkOptions(), func);
    }

    protected virtual async Task<TResult> WithUnitOfWorkAsync<TResult>(AbpUnitOfWorkOptions options, Func<Task<TResult>> func)
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            using (var uow = uowManager.Begin(options))
            {
                var result = await func();
                await uow.CompleteAsync();
                return result;
            }
        }
    }
}

public static class TestConfigurationSnapshotCache
{
    private static readonly ConcurrentDictionary<string, KeyValuePair<string, string?>[]> SnapshotByBaseDir =
        new(StringComparer.OrdinalIgnoreCase);

    public static IConfigurationRoot Build()
    {
        var baseDir = Directory.GetCurrentDirectory();
        var pairs = SnapshotByBaseDir.GetOrAdd(baseDir, static dir =>
        {
            var fileRoot = new ConfigurationBuilder()
                .SetBasePath(dir)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false)
                .Build();

            return fileRoot.AsEnumerable()
                .Where(kv => kv.Value != null)
                .ToArray();
        });

        return new ConfigurationBuilder()
            .AddInMemoryCollection(pairs)
            .Build();
    }
}
