using System.Collections.Generic;
using SwapMid.Data;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace SwapMid.Domain.Data;

public class SwapMidDbMigrationServiceTests
{
    [Fact]
    public void ResolveAdminSeedCredentials_ShouldPreferConfiguredValues()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Identity:AdminEmail"] = "uat-admin@example.test",
            ["Identity:AdminPassword"] = "Uat-Only-Strong-Password"
        });

        var (email, password) = SwapMidDbMigrationService
            .ResolveAdminSeedCredentials(configuration);

        email.ShouldBe("uat-admin@example.test");
        password.ShouldBe("Uat-Only-Strong-Password");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "\t")]
    public void ResolveAdminSeedCredentials_ShouldFallbackWhenConfiguredValuesAreBlank(
        string? configuredEmail,
        string? configuredPassword)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Identity:AdminEmail"] = configuredEmail,
            ["Identity:AdminPassword"] = configuredPassword
        });

        var (email, password) = SwapMidDbMigrationService
            .ResolveAdminSeedCredentials(configuration);

        email.ShouldBe(SwapMidConsts.AdminEmailDefaultValue);
        password.ShouldBe(SwapMidConsts.AdminPasswordDefaultValue);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
