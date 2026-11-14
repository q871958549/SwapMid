using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace SwapMid.Infrastructure;

/// <summary>
/// <see cref="SqlServerConnectionStringHelper"/> 行为快照（ADR-0010 §4.4-A 改造前后behavior
/// 基线，docs/external-data-gateway-blueprint.md §7.1）——此前零覆盖，本次新增默认命令超时
/// 注入前先固化既有 jdbc: 归一化 / alias 回退 / 配置节回退行为，防收编时静默回归。
/// </summary>
public class SqlServerConnectionStringHelperTests
{
    [Fact]
    public void 标准ADO格式串_直接命中ConnectionStrings节_原样返回并注入默认超时()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OAQuery"] = "Server=127.0.0.1;Database=OA;User ID=sa;Password=x;",
        });

        var result = SqlServerConnectionStringHelper.GetConfiguredConnectionString(config, "OAQuery", "OA-query");

        result.ShouldNotBeNull();
        new SqlConnectionStringBuilder(result!).CommandTimeout.ShouldBe(30);
    }

    [Fact]
    public void jdbc格式串_归一化为ADO格式_并注入默认超时()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OAQuery"] = "jdbc:sqlserver://10.168.1.8:1433;databaseName=OA;username=sa;password=x;",
        });

        var result = SqlServerConnectionStringHelper.GetConfiguredConnectionString(config, "OAQuery", "OA-query");

        result.ShouldNotBeNull();
        var builder = new SqlConnectionStringBuilder(result!);
        builder.DataSource.ShouldBe("10.168.1.8,1433");
        builder.InitialCatalog.ShouldBe("OA");
        builder.CommandTimeout.ShouldBe(30);
    }

    [Fact]
    public void 直连节缺失_回退alias节_仍能取到串()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["OA-query:url"] = "jdbc:sqlserver://10.168.1.8:1433;databaseName=OA;",
            ["OA-query:username"] = "sa",
            ["OA-query:password"] = "x",
        });

        var result = SqlServerConnectionStringHelper.GetConfiguredConnectionString(config, "OAQuery", "OA-query");

        result.ShouldNotBeNull();
        new SqlConnectionStringBuilder(result!).UserID.ShouldBe("sa");
    }

    [Fact]
    public void 直连与alias节均缺失_回退ExternalDataSources配置节()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ExternalDataSources:OAQuery:url"] = "jdbc:sqlserver://10.168.1.8:1433;databaseName=OA;",
        });

        var result = SqlServerConnectionStringHelper.GetConfiguredConnectionString(config, "OAQuery", "OA-query");

        result.ShouldNotBeNull();
    }

    [Fact]
    public void 全部节均缺失_返回null()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var result = SqlServerConnectionStringHelper.GetConfiguredConnectionString(config, "OAQuery", "OA-query");

        result.ShouldBeNull();
    }

    [Fact]
    public void 逐源CommandTimeoutSeconds配置优先于全局Defaults()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OAQuery"] = "Server=127.0.0.1;Database=OA;",
            ["ExternalDataSources:OAQuery:CommandTimeoutSeconds"] = "8",
            ["ExternalDataSources:Defaults:CommandTimeoutSeconds"] = "60",
        });

        var result = SqlServerConnectionStringHelper.GetConfiguredConnectionString(config, "OAQuery", "OA-query");

        new SqlConnectionStringBuilder(result!).CommandTimeout.ShouldBe(8);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
