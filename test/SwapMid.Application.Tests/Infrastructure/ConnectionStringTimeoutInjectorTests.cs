using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Shouldly;
using Xunit;

namespace SwapMid.Infrastructure;

/// <summary>
/// 连接串级默认命令超时注入纯函数测试（ADR-0010 §4.2 · docs/external-data-gateway-blueprint.md §7.1）。
/// </summary>
public class ConnectionStringTimeoutInjectorTests
{
    [Fact]
    public void SqlServer串无关键字_追加后CommandTimeout生效()
    {
        var result = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(
            "Server=127.0.0.1;Database=ERP;User ID=sa;Password=x;", 45);

        new SqlConnectionStringBuilder(result).CommandTimeout.ShouldBe(45);
    }

    [Fact]
    public void SqlServer串已含CommandTimeout_原样返回不覆盖显式配置()
    {
        const string original = "Server=127.0.0.1;Database=ERP;Command Timeout=99;";
        var result = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(original, 45);

        result.ShouldBe(original);
        new SqlConnectionStringBuilder(result).CommandTimeout.ShouldBe(99);
    }

    [Fact]
    public void SqlServer串大小写变体的CommandTimeout也能识别_不重复追加()
    {
        const string original = "Server=127.0.0.1;command timeout=20;";
        var result = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(original, 45);

        result.ShouldBe(original);
    }

    [Fact]
    public void SqlServer串仅含ConnectTimeout_不误判为已含CommandTimeout_仍追加()
    {
        // Connect Timeout（建连超时）与 Command Timeout（命令超时）是两个不同关键字，不能互相掩盖。
        var result = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(
            "Server=127.0.0.1;Connect Timeout=15;", 45);

        var builder = new SqlConnectionStringBuilder(result);
        builder.CommandTimeout.ShouldBe(45);
        builder.ConnectTimeout.ShouldBe(15);
    }

    [Fact]
    public void Postgres串无关键字_追加后CommandTimeout生效()
    {
        var result = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(
            "Host=127.0.0.1;Port=5432;Database=swapmid;Username=u;Password=p;", 30);

        new NpgsqlConnectionStringBuilder(result).CommandTimeout.ShouldBe(30);
    }

    [Fact]
    public void MySQL串无关键字_追加后DefaultCommandTimeout生效()
    {
        var result = ConnectionStringTimeoutInjector.AppendMySqlCommandTimeout(
            "Server=127.0.0.1;Database=swapmid;Uid=u;Pwd=p;", 30);

        new MySqlConnectionStringBuilder(result).DefaultCommandTimeout.ShouldBe(30u);
    }

    [Fact]
    public void MySQL串已含DefaultCommandTimeout_原样返回()
    {
        const string original = "Server=127.0.0.1;Default Command Timeout=99;";
        var result = ConnectionStringTimeoutInjector.AppendMySqlCommandTimeout(original, 30);

        result.ShouldBe(original);
    }

    [Fact]
    public void SqlServer正则不被MySQL的DefaultCommandTimeout误伤_仍追加SqlServer关键字()
    {
        // 负向后顾断言的正确性：SqlServer/PG 的 "command timeout=" 检测不应被 "default command timeout=" 命中。
        // 该场景现实中不会混用，但作为回归防线验证正则边界。
        var result = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(
            "Server=127.0.0.1;Default Command Timeout=99;", 45);

        result.ShouldContain("Command Timeout=45");
    }
}
