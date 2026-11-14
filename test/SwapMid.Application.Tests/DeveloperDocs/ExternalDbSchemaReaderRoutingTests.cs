using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DeveloperDocs;

public class ExternalDbSchemaReaderRoutingTests
{
    [Fact]
    public void GetSources_Should_Read_Configured_External_Sources()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DeveloperDocs:ExternalDbSources:0:Key"] = "ErpDemo",
            ["DeveloperDocs:ExternalDbSources:0:DisplayName"] = "ERP 示例库",
            ["DeveloperDocs:ExternalDbSources:0:Dialect"] = "PostgreSql",
            ["DeveloperDocs:ExternalDbSources:0:ConnectionStringName"] = "ErpDemo",
            ["ConnectionStrings:ErpDemo"] = "Host=127.0.0.1;Database=erp;",
        });

        var sut = new ExternalDbSchemaReader(
            config,
            Substitute.For<IExternalDbConnectionFactory>(),
            NullLogger<ExternalDbSchemaReader>.Instance);

        var sources = sut.GetSources();
        sources.Count.ShouldBe(1);
        sources[0].Key.ShouldBe("ErpDemo");
        sources[0].DisplayName.ShouldBe("ERP 示例库");
        sources[0].Dialect.ShouldBe("PostgreSql");
    }

    [Fact]
    public async Task Blank_ConnectionString_Should_ShortCircuit_Without_Calling_Factory()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DeveloperDocs:ExternalDbSources:0:Key"] = "UnconfiguredDb",
            ["DeveloperDocs:ExternalDbSources:0:DisplayName"] = "未配置库",
            ["DeveloperDocs:ExternalDbSources:0:Dialect"] = "SqlServer",
        });
        var factory = Substitute.For<IExternalDbConnectionFactory>();
        var sut = new ExternalDbSchemaReader(config, factory, NullLogger<ExternalDbSchemaReader>.Instance);

        var tables = await sut.ReadTablesAsync("UnconfiguredDb");
        tables.ShouldBeEmpty();
        await factory.DidNotReceiveWithAnyArgs()
            .OpenSqlServerConnectionAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Unknown_SourceKey_Should_Throw_UserFriendlyException()
    {
        var sut = new ExternalDbSchemaReader(
            BuildConfig(new Dictionary<string, string?>()),
            Substitute.For<IExternalDbConnectionFactory>(),
            NullLogger<ExternalDbSchemaReader>.Instance);

        await Should.ThrowAsync<UserFriendlyException>(() => sut.ReadTablesAsync("NonExistent"));
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public async Task Configured_Dialect_Should_Route_To_Matching_Factory_Method(string dialect)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["DeveloperDocs:ExternalDbSources:0:Key"] = "Ext1",
            ["DeveloperDocs:ExternalDbSources:0:DisplayName"] = "外部库",
            ["DeveloperDocs:ExternalDbSources:0:Dialect"] = dialect,
            ["ConnectionStrings:Ext1"] = "DummyConnectionString",
        });

        var factory = Substitute.For<IExternalDbConnectionFactory>();
        factory.OpenSqlServerConnectionAsync(
                "DummyConnectionString",
                "schema:Ext1",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ExternalDbConnectionLease>>(_ => throw new InvalidOperationException("offline-test"));
        factory.OpenPostgreSqlConnectionAsync(
                "DummyConnectionString",
                "schema:Ext1",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ExternalDbConnectionLease>>(_ => throw new InvalidOperationException("offline-test"));
        factory.OpenMySqlConnectionAsync(
                "DummyConnectionString",
                "schema:Ext1",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ExternalDbConnectionLease>>(_ => throw new InvalidOperationException("offline-test"));

        var sut = new ExternalDbSchemaReader(config, factory, NullLogger<ExternalDbSchemaReader>.Instance);
        var result = await sut.ReadTablesAsync("Ext1");
        result.ShouldBeEmpty();

        if (dialect == "SqlServer")
        {
            await factory.Received(1).OpenSqlServerConnectionAsync(
                "DummyConnectionString",
                "schema:Ext1",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>());
        }
        else if (dialect == "PostgreSql")
        {
            await factory.Received(1).OpenPostgreSqlConnectionAsync(
                "DummyConnectionString",
                "schema:Ext1",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>());
        }
        else
        {
            await factory.Received(1).OpenMySqlConnectionAsync(
                "DummyConnectionString",
                "schema:Ext1",
                Arg.Any<int>(),
                Arg.Any<CancellationToken>());
        }
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
