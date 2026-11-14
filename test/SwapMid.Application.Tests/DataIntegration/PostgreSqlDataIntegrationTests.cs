using System.Text.Json.Nodes;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class PostgreSqlDataIntegrationTests
{
    [Theory]
    [InlineData("postgresql")]
    [InlineData("PostgreSQL")]
    [InlineData("postgres")]
    [InlineData("npgsql")]
    public void ParseForWrite_Should_Accept_PostgreSql_Aliases_And_Default_Port_5432(string alias)
    {
        var json = JsonNode.Parse(
            $$"""
            {
              "provider": "{{alias}}",
              "host": "pg.internal",
              "database": "swapmid_dw",
              "username": "etl_reader",
              "password": "secret",
              "encrypt": true,
              "trustServerCertificate": false
            }
            """)!.AsObject();

        var config = RelationalDataSourceConfigContract.ParseForWrite(json);

        config.Provider.ShouldBe("postgresql");
        config.Port.ShouldBe(5432);
        config.Database.ShouldBe("swapmid_dw");
        config.Encrypt.ShouldBeTrue();
        config.TrustServerCertificate.ShouldBeFalse();
    }

    [Fact]
    public void QuoteIdentifier_Should_Use_DoubleQuotes_For_PostgreSql()
    {
        var probe = new RelationalDataSourceConnection.Probe
        {
            Provider = "postgresql",
            Host = "127.0.0.1",
            Database = "dw",
        };

        var quoted = RelationalDataSourceConnection.QuoteIdentifier("analytics.daily_orders", probe);
        quoted.ShouldBe("\"analytics\".\"daily_orders\"");
    }

    [Theory]
    [InlineData("SELECT id, code, amount FROM demo_etl.source_orders WHERE amount > 0")]
    [InlineData("WITH RECURSIVE t(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM t WHERE n < 5) SELECT n FROM t")]
    public void EtlSourceQueryGuard_Should_Accept_Safe_PostgreSql_Select_And_Recursive_Cte(string sql)
    {
        Should.NotThrow(() => EtlSourceQueryGuard.Validate(sql, "postgresql"));
    }

    [Theory]
    [InlineData("SELECT $$unsafe$$")]
    [InlineData("SELECT $tag$unsafe$tag$")]
    [InlineData("SELECT $1 FROM demo_etl.source_orders")]
    [InlineData("SELECT U&'\\0041' FROM demo_etl.source_orders")]
    [InlineData("SELECT * FROM demo_etl.source_orders FOR UPDATE")]
    [InlineData("SELECT pg_sleep(5)")]
    [InlineData("SELECT \"pg_sleep\"(5)")]
    [InlineData("SELECT nextval('seq_orders')")]
    [InlineData("SELECT setval('seq_orders', 10)")]
    [InlineData("SELECT pg_read_file('/etc/passwd')")]
    [InlineData("SELECT 1; SELECT 2")]
    public void EtlSourceQueryGuard_Should_Reject_Dangerous_PostgreSql_Constructs(string sql)
    {
        Should.Throw<BusinessException>(() => EtlSourceQueryGuard.Validate(sql, "postgresql"));
    }
}
