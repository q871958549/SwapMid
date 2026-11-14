using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Npgsql;
using Shouldly;
using Xunit;

namespace SwapMid.Infrastructure;

public class ExternalDbConnectionFactoryTests
{
    [Fact]
    public void SqlServerConnectionStringHelper_Should_Default_Encrypt_And_TrustServerCertificate_Safely()
    {
        var normalized = SqlServerConnectionStringHelper.Normalize(
            "Server=db.example.test;Database=swapmid_ext;User ID=reader;Password=secret");
        var withTimeout = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(normalized, 25);

        var builder = new SqlConnectionStringBuilder(withTimeout);
        builder.DataSource.ShouldBe("db.example.test");
        builder.CommandTimeout.ShouldBe(25);
    }

    [Fact]
    public void ConnectionStringTimeoutInjector_Should_Inject_Postgres_And_MySql_CommandTimeout()
    {
        var pg = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(
            "Host=pg.example.test;Database=swapmid_dw;Username=reader;",
            45);
        new NpgsqlConnectionStringBuilder(pg).CommandTimeout.ShouldBe(45);

        var mysql = ConnectionStringTimeoutInjector.AppendMySqlCommandTimeout(
            "Server=mysql.example.test;Database=swapmid_dw;User ID=reader;",
            40);
        mysql.ShouldContain("Default Command Timeout=40");
    }

    [Fact]
    public async Task OpenPostgreSqlConnectionAsync_Should_Reject_Immediately_When_Concurrency_Gate_Exhausted()
    {
        var gate = Substitute.For<IExternalDataSourceConcurrencyGate>();
        gate.TryAcquireAsync("extdb:erp-dw", 3, Arg.Any<CancellationToken>())
            .Returns((IAsyncDisposable?)null);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalDb:erp-dw:MaxConcurrency"] = "3",
            })
            .Build();

        var sut = new ExternalDbConnectionFactory(gate, config);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            sut.OpenPostgreSqlConnectionAsync(
                "Host=127.0.0.1;Port=1;Database=dw;Username=u;Password=p;",
                "erp-dw"));

        ex.Message.ShouldContain("erp-dw");
    }
}
