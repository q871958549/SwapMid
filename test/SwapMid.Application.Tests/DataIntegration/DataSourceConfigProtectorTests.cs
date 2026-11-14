using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// DataSource Config 的白名单读边界与强类型写边界测试。
/// </summary>
public class DataSourceConfigProtectorTests
{
    private const string StoredRelational = """
        {
          "provider":"sqlserver",
          "host":"DB.EXAMPLE.TEST.",
          "port":1433,
          "database":"Han",
          "username":"reader",
          "password":"old-secret",
          "encrypt":true,
          "trustServerCertificate":false,
          "url":"jdbc:sqlserver://db.example.test;Password=url-secret",
          "driverClassName":"untrusted-driver",
          "unknown":{"token":"unknown-secret"}
        }
        """;

    [Fact]
    public void 关系型读响应_只重建七个白名单字段()
    {
        var result = DataSourceConfigProtector.SanitizeForRead(
            DataSourceType.RelationalDatabase,
            StoredRelational);

        result.HasSecret.ShouldBeTrue();
        foreach (var value in new[]
                 {
                     "old-secret", "url-secret", "untrusted-driver", "unknown-secret",
                     "password", "url", "driverClassName", "unknown"
                 })
        {
            result.Config.ShouldNotContain(value);
        }

        var json = JsonNode.Parse(result.Config)!.AsObject();
        json.Count.ShouldBe(7);
        json["provider"]!.GetValue<string>().ShouldBe("sqlserver");
        json["host"]!.GetValue<string>().ShouldBe("db.example.test");
        json["port"]!.GetValue<int>().ShouldBe(1433);
        json["database"]!.GetValue<string>().ShouldBe("Han");
        json["username"]!.GetValue<string>().ShouldBe("reader");
        json["encrypt"]!.GetValue<bool>().ShouldBeTrue();
        json["trustServerCertificate"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void 关系型仅有白名单字段且无密码_HasSecret为false()
    {
        const string raw = """
            {"provider":"mysql","host":"db.example.test","port":3306,"database":"Han","username":"reader","encrypt":false,"trustServerCertificate":true}
            """;

        var result = DataSourceConfigProtector.SanitizeForRead(
            DataSourceType.RelationalDatabase,
            raw);

        result.HasSecret.ShouldBeFalse();
        JsonNode.Parse(result.Config)!.AsObject().Count.ShouldBe(7);
    }

    [Fact]
    public void 目标物理资源组应安全回显持久化且不属于凭据作用域()
    {
        const string stored = """
            {"provider":"sqlserver","host":"db-a.example.test","port":1433,"database":"Han","username":"reader","password":"old-secret","targetResourceGroup":"prod-han-primary"}
            """;
        const string proposed = """
            {"provider":"sqlserver","host":"db-a.example.test","port":1433,"database":"Han","username":"reader","targetResourceGroup":"prod-han-dr"}
            """;

        var read = DataSourceConfigProtector.SanitizeForRead(
            DataSourceType.RelationalDatabase,
            stored);
        var readJson = JsonNode.Parse(read.Config)!.AsObject();
        readJson["targetResourceGroup"]!.GetValue<string>()
            .ShouldBe("prod-han-primary");
        read.Config.ShouldNotContain("old-secret");

        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.RelationalDatabase,
            stored,
            DataSourceType.RelationalDatabase,
            proposed);
        var mergedJson = JsonNode.Parse(merged)!.AsObject();
        mergedJson["targetResourceGroup"]!.GetValue<string>()
            .ShouldBe("prod-han-dr");
        mergedJson["password"]!.GetValue<string>()
            .ShouldBe("old-secret");
    }

    [Theory]
    [InlineData("contains space")]
    [InlineData("../escape")]
    [InlineData("x/y")]
    public void 非法目标物理资源组应拒绝写入(string value)
    {
        var raw = $$"""
            {"provider":"sqlserver","host":"db.example.test","port":1433,"database":"Han","username":"reader","targetResourceGroup":"{{value}}"}
            """;

        Should.Throw<DataSourceConfigFormatException>(() =>
            DataSourceConfigProtector.NormalizeForWrite(
                DataSourceType.RelationalDatabase,
                raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"password=do-not-return\"")]
    [InlineData("{\"password\":\"do-not-return\"")]
    public void 非法或非对象配置_读响应失败关闭(string? raw)
    {
        var result = DataSourceConfigProtector.SanitizeForRead(
            DataSourceType.RelationalDatabase,
            raw);

        result.Config.ShouldBe(DataSourceConfigProtector.SafeEmptyConfig);
        result.HasSecret.ShouldBeTrue();
        result.Config.ShouldNotContain("do-not-return");
    }

    [Theory]
    [InlineData(DataSourceType.RestApi)]
    [InlineData(DataSourceType.File)]
    public void 未实装类型_非空存量始终作为不透明配置返回(DataSourceType type)
    {
        const string raw = """
            {"baseUrl":"https://api-user:api-pass@example.test","headers":{"Authorization":"Bearer secret"}}
            """;

        var result = DataSourceConfigProtector.SanitizeForRead(type, raw);

        result.Config.ShouldBe(DataSourceConfigProtector.SafeEmptyConfig);
        result.HasSecret.ShouldBeTrue();
        result.Config.ShouldNotContain("api-user");
        result.Config.ShouldNotContain("secret");
    }

    [Fact]
    public void 未实装类型_空对象没有不透明配置()
    {
        var result = DataSourceConfigProtector.SanitizeForRead(DataSourceType.RestApi, "{}");

        result.Config.ShouldBe(DataSourceConfigProtector.SafeEmptyConfig);
        result.HasSecret.ShouldBeFalse();
    }

    [Fact]
    public void 关系型写入_只保存强类型字段和密码()
    {
        var normalized = DataSourceConfigProtector.NormalizeForWrite(
            DataSourceType.RelationalDatabase,
            StoredRelational);

        normalized.ShouldContain("old-secret");
        normalized.ShouldNotContain("url-secret");
        normalized.ShouldNotContain("untrusted-driver");
        normalized.ShouldNotContain("unknown-secret");
        JsonNode.Parse(normalized)!.AsObject().Count.ShouldBe(8);
    }

    [Fact]
    public void 关系型作用域不变_缺失密码沿用旧值()
    {
        const string proposed = """
            {"provider":"mssql","host":"db.example.test","port":"1433","database":"Han","username":"reader","encrypt":true,"trustServerCertificate":false}
            """;

        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.RelationalDatabase,
            StoredRelational,
            DataSourceType.RelationalDatabase,
            proposed);

        merged.ShouldContain("old-secret");
        merged.ShouldNotContain("untrusted-driver");
        JsonNode.Parse(merged)!.AsObject().Count.ShouldBe(8);
    }

    [Theory]
    [InlineData("provider", "mysql")]
    [InlineData("host", "new-db.example.test")]
    [InlineData("port", "1434")]
    [InlineData("database", "Other")]
    [InlineData("username", "other-reader")]
    public void 关系型目标字段变化_空密码拒绝沿用(string field, string value)
    {
        var proposed = JsonNode.Parse(
            DataSourceConfigProtector.SanitizeForRead(
                DataSourceType.RelationalDatabase,
                StoredRelational).Config)!.AsObject();
        if (field == "port")
        {
            proposed[field] = int.Parse(value);
        }
        else
        {
            proposed[field] = value;
        }

        Should.Throw<DataSourceCredentialScopeChangedException>(() =>
            DataSourceConfigProtector.MergeForWrite(
                DataSourceType.RelationalDatabase,
                StoredRelational,
                DataSourceType.RelationalDatabase,
                proposed.ToJsonString()));
    }

    [Theory]
    [InlineData("encrypt", false)]
    [InlineData("trustServerCertificate", true)]
    public void 关系型TLS姿态弱化_空密码拒绝沿用(string field, bool value)
    {
        var proposed = JsonNode.Parse(
            DataSourceConfigProtector.SanitizeForRead(
                DataSourceType.RelationalDatabase,
                StoredRelational).Config)!.AsObject();
        proposed[field] = value;

        Should.Throw<DataSourceCredentialScopeChangedException>(() =>
            DataSourceConfigProtector.MergeForWrite(
                DataSourceType.RelationalDatabase,
                StoredRelational,
                DataSourceType.RelationalDatabase,
                proposed.ToJsonString()));
    }

    [Fact]
    public void 关系型作用域变化_显式新密码完整替换()
    {
        const string proposed = """
            {"provider":"sqlserver","host":"new-db.example.test","port":1433,"database":"Han","username":"reader","password":"new-secret","encrypt":true,"trustServerCertificate":false}
            """;

        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.RelationalDatabase,
            StoredRelational,
            DataSourceType.RelationalDatabase,
            proposed);

        merged.ShouldContain("new-secret");
        merged.ShouldNotContain("old-secret");
    }

    [Fact]
    public void 关系型即使旧配置无密码_作用域变化也要求显式新密码()
    {
        const string stored = """
            {"provider":"sqlserver","host":"old-db","port":1433,"database":"Han","username":"reader"}
            """;
        const string proposed = """
            {"provider":"sqlserver","host":"new-db","port":1433,"database":"Han","username":"reader"}
            """;

        Should.Throw<DataSourceCredentialScopeChangedException>(() =>
            DataSourceConfigProtector.MergeForWrite(
                DataSourceType.RelationalDatabase,
                stored,
                DataSourceType.RelationalDatabase,
                proposed));
    }

    [Fact]
    public void 仅connectionString关系型存量_无法证明作用域时拒绝沿用()
    {
        const string stored = """
            {"connectionString":"Server=old-db;Database=Han;User Id=reader;Password=old-secret"}
            """;
        const string proposed = """
            {"provider":"sqlserver","host":"old-db","port":1433,"database":"Han","username":"reader"}
            """;

        Should.Throw<DataSourceCredentialScopeChangedException>(() =>
            DataSourceConfigProtector.MergeForWrite(
                DataSourceType.RelationalDatabase,
                stored,
                DataSourceType.RelationalDatabase,
                proposed));
    }

    [Fact]
    public void 畸形关系型存量_显式新密码可完整替换并自愈()
    {
        const string proposed = """
            {"provider":"sqlserver","host":"db.example.test","port":1433,"database":"Han","username":"reader","password":"new-secret"}
            """;

        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.RelationalDatabase,
            "{\"password\":\"broken-old-secret\"",
            DataSourceType.RelationalDatabase,
            proposed);

        merged.ShouldContain("new-secret");
        merged.ShouldNotContain("broken-old-secret");
    }

    [Fact]
    public void 畸形不透明存量_只允许完整非空对象替换()
    {
        const string stored = "{\"token\":\"broken-old-secret\"";

        Should.Throw<DataSourceConfigReplacementRequiredException>(() =>
            DataSourceConfigProtector.MergeForWrite(
                DataSourceType.RestApi,
                stored,
                DataSourceType.RestApi,
                "{}"));

        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.RestApi,
            stored,
            DataSourceType.RestApi,
            """{"baseUrl":"https://new.example.test","token":"new-secret"}""");

        merged.ShouldContain("new-secret");
        merged.ShouldNotContain("broken-old-secret");
    }

    [Fact]
    public void 畸形不透明存量_切换关系型是完整替换且无需猜测旧秘密()
    {
        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.RestApi,
            "{\"token\":\"broken-old-secret\"",
            DataSourceType.RelationalDatabase,
            """{"provider":"sqlserver","host":"db.example.test","port":1433,"database":"Han","username":"reader"}""");

        merged.ShouldContain("db.example.test");
        merged.ShouldNotContain("broken-old-secret");
    }

    [Theory]
    [InlineData(DataSourceType.RestApi)]
    [InlineData(DataSourceType.File)]
    public void 未实装类型_脱敏空对象回传时拒绝擦除旧配置(DataSourceType type)
    {
        const string stored = """{"opaque":"old-secret"}""";

        Should.Throw<DataSourceConfigReplacementRequiredException>(() =>
            DataSourceConfigProtector.MergeForWrite(type, stored, type, "{}"));
    }

    [Theory]
    [InlineData(DataSourceType.RestApi)]
    [InlineData(DataSourceType.File)]
    public void 未实装类型_完整非空配置作为整体替换且不混入旧秘密(DataSourceType type)
    {
        const string stored = """{"opaque":"old-secret"}""";
        const string proposed = """{"replacement":"new-complete-config"}""";

        var merged = DataSourceConfigProtector.MergeForWrite(
            type,
            stored,
            type,
            proposed);

        merged.ShouldContain("new-complete-config");
        merged.ShouldNotContain("old-secret");
    }

    [Fact]
    public void 非法写配置_拒绝而不是保存或回显原文()
    {
        Should.Throw<DataSourceConfigFormatException>(() =>
            DataSourceConfigProtector.NormalizeForWrite(
                DataSourceType.RestApi,
                "{\"password\":\"secret\""));
    }

    [Fact]
    public void 未定义数据源类型_拒绝写入()
    {
        Should.Throw<DataSourceConfigFormatException>(() =>
            DataSourceConfigProtector.NormalizeForWrite(
                (DataSourceType)999,
                "{}"));
    }

    [Fact]
    public void 规范化后超过存储上限_在进入实体前返回配置错误()
    {
        var password = new string('密', 400);
        var config = $$"""{"provider":"sqlserver","host":"db","password":"{{password}}"}""";
        config.Length.ShouldBeLessThanOrEqualTo(DataSourceConsts.MaxConfigLength);

        Should.Throw<DataSourceConfigFormatException>(() =>
            DataSourceConfigProtector.NormalizeForWrite(
                DataSourceType.RelationalDatabase,
                config));
    }

    [Theory]
    [InlineData("""{"provider":"sqlserver","host":"db","port":"secret"}""")]
    [InlineData("""{"provider":"sqlserver","host":"db","encrypt":"secret"}""")]
    [InlineData("""{"provider":"sqlserver","host":"db","password":{"value":"secret"}}""")]
    public void 关系型字段类型不符合强契约_拒绝写入(string config)
    {
        Should.Throw<DataSourceConfigFormatException>(() =>
            DataSourceConfigProtector.NormalizeForWrite(
                DataSourceType.RelationalDatabase,
                config));
    }
}
