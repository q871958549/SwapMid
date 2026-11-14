using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

public class DomainDependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(SwapMidDomainModule).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(SwapMidApplicationModule).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(SwapMidApplicationContractsModule).Assembly;

    [Fact]
    public void Infrastructure_不得反向依赖任何业务域()
    {
        var forbiddenDomains = new[]
        {
            "SwapMid.AccountManagement",
            "SwapMid.ApiManagement",
            "SwapMid.DataIntegration",
            "SwapMid.DeveloperDocs",
            "SwapMid.EnterpriseManagement",
            "SwapMid.Layout",
            "SwapMid.MessageBus",
            "SwapMid.Notification",
            "SwapMid.Scheduling",
            "SwapMid.SystemManagement",
        };

        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, ContractsAssembly })
        {
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespace("SwapMid.Infrastructure")
                .ShouldNot().HaveDependencyOnAny(forbiddenDomains)
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"[{assembly.GetName().Name}] Infrastructure 反向依赖了业务域: " +
                string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }
    }

    [Fact]
    public void SharedKernel_不得依赖任何业务域()
    {
        var forbidden = new[]
        {
            "SwapMid.AccountManagement",
            "SwapMid.ApiManagement",
            "SwapMid.DataIntegration",
            "SwapMid.DeveloperDocs",
            "SwapMid.EnterpriseManagement",
            "SwapMid.Layout",
            "SwapMid.MessageBus",
            "SwapMid.Notification",
            "SwapMid.Scheduling",
            "SwapMid.SystemManagement",
        };

        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, ContractsAssembly })
        {
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespace("SwapMid.SharedKernel")
                .ShouldNot().HaveDependencyOnAny(forbidden)
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"[{assembly.GetName().Name}] SharedKernel 反向依赖了业务域: " +
                string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }
    }

    [Theory]
    [InlineData("SwapMid.ApiManagement")]
    [InlineData("SwapMid.DataIntegration")]
    [InlineData("SwapMid.MessageBus")]
    public void 平台纯技术能力域_不得依赖企业组织或账号业务域(string platformNamespace)
    {
        var businessDomains = new[]
        {
            "SwapMid.EnterpriseManagement",
            "SwapMid.AccountManagement",
        };

        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, ContractsAssembly })
        {
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespace(platformNamespace)
                .ShouldNot().HaveDependencyOnAny(businessDomains)
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"[{assembly.GetName().Name}] {platformNamespace} 反向依赖了组织/账号域: " +
                string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }
    }
}
