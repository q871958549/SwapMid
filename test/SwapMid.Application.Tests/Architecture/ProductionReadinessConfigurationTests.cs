using System;
using System.IO;
using System.Text.Json;
using SwapMid.Infrastructure;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

public class ProductionReadinessConfigurationTests
{
    [Fact]
    public void Base_Appsettings_Must_Keep_ApiGateway_Disabled_Until_Explicit_OptIn()
    {
        var root = FindRepoRoot();
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "src", "SwapMid.HttpApi.Host", "appsettings.json")));

        document.RootElement
            .GetProperty("ApiGateway")
            .GetProperty("Enabled")
            .GetBoolean()
            .ShouldBeFalse();
    }

    [Fact]
    public void OpenTelemetry_HttpClient_Instrumentation_Must_Filter_Sensitive_Outbound_Requests()
    {
        var hostModule = HostModuleSource.ReadAll();

        hostModule.ShouldContain("SensitiveOutboundTelemetryFilter.ShouldTrace");
    }

    [Fact]
    public void Host_Must_Configure_Real_Redis_Distributed_Lock_Provider()
    {
        var root = FindRepoRoot();
        var hostCsproj = File.ReadAllText(
            Path.Combine(root, "src", "SwapMid.HttpApi.Host", "SwapMid.HttpApi.Host.csproj"));
        var hostModule = HostModuleSource.ReadAll();

        hostCsproj.ShouldContain("Volo.Abp.DistributedLocking");
        hostCsproj.ShouldContain("DistributedLock.Redis");
        hostModule.ShouldContain("typeof(AbpDistributedLockingModule)");
        hostModule.ShouldContain("RedisDistributedSynchronizationProvider");
        hostModule.ShouldContain("AddSingleton<IDistributedLockProvider>");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwapMid.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find SwapMid.slnx.");
    }
}
