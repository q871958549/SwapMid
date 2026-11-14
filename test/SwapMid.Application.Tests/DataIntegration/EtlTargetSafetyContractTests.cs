using System;
using MySqlConnector;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class EtlTargetSafetyContractTests
{
    [Fact]
    public void Append可省略目标普通列但不得写生成列或未知列()
    {
        var target = new[]
        {
            new EtlTargetColumnContract.Column(
                "id",
                IsWritable: true,
                IsIdentity: false),
            new EtlTargetColumnContract.Column(
                "created_at",
                IsWritable: false,
                IsIdentity: false),
            new EtlTargetColumnContract.Column(
                "note",
                IsWritable: true,
                IsIdentity: false),
        };

        Should.NotThrow(() =>
            EtlTargetColumnContract.ValidateProjection(
                ["id"],
                target,
                truncate: false));
        ShouldRejectProjection(
            ["created_at"],
            target,
            truncate: false);
        ShouldRejectProjection(
            ["missing"],
            target,
            truncate: false);
    }

    [Fact]
    public void Truncate必须覆盖全部普通可写列()
    {
        var target = new[]
        {
            new EtlTargetColumnContract.Column(
                "id",
                IsWritable: true,
                IsIdentity: false),
            new EtlTargetColumnContract.Column(
                "note",
                IsWritable: true,
                IsIdentity: false),
            new EtlTargetColumnContract.Column(
                "computed_total",
                IsWritable: false,
                IsIdentity: false),
        };

        Should.NotThrow(() =>
            EtlTargetColumnContract.ValidateProjection(
                ["note", "id"],
                target,
                truncate: true));
        ShouldRejectProjection(
            ["id"],
            target,
            truncate: true);
    }

    [Fact]
    public void Truncate遇到Identity或AutoIncrement必须拒绝()
    {
        var target = new[]
        {
            new EtlTargetColumnContract.Column(
                "id",
                IsWritable: false,
                IsIdentity: true),
            new EtlTargetColumnContract.Column(
                "value",
                IsWritable: true,
                IsIdentity: false),
        };

        Should.NotThrow(() =>
            EtlTargetColumnContract.ValidateProjection(
                ["value"],
                target,
                truncate: false));
        ShouldRejectProjection(
            ["value"],
            target,
            truncate: true);
        ShouldRejectProjection(
            ["id", "value"],
            target,
            truncate: false);
    }

    [Fact]
    public void Staging回读必须识别舍入截断与类型漂移()
    {
        EtlStagedValueGuard.AreEquivalent(
                1,
                1.000m)
            .ShouldBeTrue();
        EtlStagedValueGuard.AreEquivalent(
                1.234m,
                1.23m)
            .ShouldBeFalse();
        EtlStagedValueGuard.AreEquivalent(
                "value ",
                "value")
            .ShouldBeFalse();
        EtlStagedValueGuard.AreEquivalent(
                new byte[] { 1, 2, 3 },
                new byte[] { 1, 2 })
            .ShouldBeFalse();
        EtlStagedValueGuard.AreEquivalent(
                1d,
                1f)
            .ShouldBeFalse();
    }

    [Fact]
    public void Staging回读必须保留时间精度与DateTimeOffset原始偏移()
    {
        var instant = new DateTime(
            2026,
            7,
            27,
            12,
            30,
            45,
            DateTimeKind.Unspecified).AddTicks(1234);
        EtlStagedValueGuard.AreEquivalent(
                instant,
                instant)
            .ShouldBeTrue();
        EtlStagedValueGuard.AreEquivalent(
                instant,
                instant.AddTicks(-34))
            .ShouldBeFalse();

        var expected =
            new DateTimeOffset(instant, TimeSpan.FromHours(8));
        var sameInstantDifferentOffset =
            expected.ToOffset(TimeSpan.Zero);
        EtlStagedValueGuard.AreEquivalent(
                expected,
                sameInstantDifferentOffset)
            .ShouldBeFalse();
    }

    [Fact]
    public void 无符号整数参数必须无损拓宽为SqlClient支持类型()
    {
        EtlTargetStagingWriter.NormalizeParameterValue(
                (sbyte)-128)
            .ShouldBe((short)-128);
        EtlTargetStagingWriter.NormalizeParameterValue(
                ushort.MaxValue)
            .ShouldBe((int)ushort.MaxValue);
        EtlTargetStagingWriter.NormalizeParameterValue(
                uint.MaxValue)
            .ShouldBe((long)uint.MaxValue);
        EtlTargetStagingWriter.NormalizeParameterValue(
                ulong.MaxValue)
            .ShouldBe((decimal)ulong.MaxValue);
    }

    [Fact]
    public void MySql库名比较必须遵循服务器大小写模式()
    {
        EtlTargetSession.IsSameMySqlDatabase(
                "Factory",
                "factory",
                lowerCaseTableNames: 0)
            .ShouldBeFalse();
        EtlTargetSession.IsSameMySqlDatabase(
                "Factory",
                "factory",
                lowerCaseTableNames: 1)
            .ShouldBeTrue();
        EtlTargetSession.IsSameMySqlDatabase(
                "Factory",
                "factory",
                lowerCaseTableNames: 2)
            .ShouldBeTrue();
        EtlTargetSession.IsSameMySqlDatabase(
                "factory",
                "factory",
                lowerCaseTableNames: 3)
            .ShouldBeFalse();
    }

    [Fact]
    public void MySql连接必须禁用会在读取边界前改值的驱动推断()
    {
        using var connection =
            (MySqlConnection)RelationalDataSourceConnection.Create(
                new RelationalDataSourceConnection.Probe
                {
                    Provider = "mysql",
                    Host = "127.0.0.1",
                    Database = "contract",
                    Username = "etl",
                });
        var options =
            new MySqlConnectionStringBuilder(
                connection.ConnectionString);

        options.TreatTinyAsBoolean.ShouldBeFalse();
        options.GuidFormat.ShouldBe(MySqlGuidFormat.None);
        options.DateTimeKind.ShouldBe(
            MySqlDateTimeKind.Unspecified);
        options.NoBackslashEscapes.ShouldBeFalse();
    }

    [Fact]
    public void MySql目标写会话必须非池化且命令超时硬中止()
    {
        const string config =
            """
            {
              "provider": "mysql",
              "host": "127.0.0.1",
              "database": "contract",
              "username": "etl"
            }
            """;
        var factory = new EtlRelationalConnectionFactory();
        using var source = (MySqlConnection)factory.Create(
            config,
            connectTimeoutSeconds: 15);
        using var target = (MySqlConnection)factory.CreateTarget(
            config,
            connectTimeoutSeconds: 15,
            commandTimeoutSeconds: 60);

        var sourceOptions = new MySqlConnectionStringBuilder(
            source.ConnectionString);
        sourceOptions.Pooling.ShouldBeTrue();

        var targetOptions = new MySqlConnectionStringBuilder(
            target.ConnectionString);
        targetOptions.Pooling.ShouldBeFalse();
        targetOptions.CancellationTimeout.ShouldBe(-1);
        targetOptions.DefaultCommandTimeout.ShouldBe(60u);
    }

    [Fact]
    public void MySql指纹只归一化未引用的顶层自增计数()
    {
        const string first = """
            CREATE TABLE `target` (
              `id` bigint NOT NULL AUTO_INCREMENT,
              `note` varchar(100) DEFAULT ' AUTO_INCREMENT=41',
              CONSTRAINT `AUTO_INCREMENT=42` CHECK (`note` <> 'AUTO_INCREMENT=43')
            ) ENGINE=InnoDB AUTO_INCREMENT=100 DEFAULT CHARSET=utf8mb4
            COMMENT='AUTO_INCREMENT=44'
            """;
        const string second = """
            CREATE TABLE `target` (
              `id` bigint NOT NULL AUTO_INCREMENT,
              `note` varchar(100) DEFAULT ' AUTO_INCREMENT=41',
              CONSTRAINT `AUTO_INCREMENT=42` CHECK (`note` <> 'AUTO_INCREMENT=43')
            ) ENGINE=InnoDB AUTO_INCREMENT=200 DEFAULT CHARSET=utf8mb4
            COMMENT='AUTO_INCREMENT=44'
            """;

        var normalized =
            EtlMySqlCreateDefinition.NormalizeForFingerprint(
                first,
                backslashEscapes: true);
        normalized.ShouldBe(
            EtlMySqlCreateDefinition.NormalizeForFingerprint(
                second,
                backslashEscapes: true));
        normalized.ShouldContain("' AUTO_INCREMENT=41'");
        normalized.ShouldContain("`AUTO_INCREMENT=42`");
        normalized.ShouldContain("'AUTO_INCREMENT=43'");
        normalized.ShouldContain("COMMENT='AUTO_INCREMENT=44'");
        normalized.ShouldContain("AUTO_INCREMENT=0");
    }

    [Theory]
    [InlineData(
        "CREATE TABLE `t` (`v` text, FULLTEXT KEY `ft` (`v`)) ENGINE=InnoDB")]
    [InlineData(
        "CREATE TABLE `t` (`v` int) ENGINE=InnoDB ROW_FORMAT=COMPRESSED")]
    [InlineData(
        "CREATE TABLE `t` (`v` int) ENGINE=InnoDB KEY_BLOCK_SIZE=8")]
    [InlineData(
        "CREATE TABLE `t` (`v` int) /*!50100 TABLESPACE `shared` */ ENGINE=InnoDB")]
    public void MySql临时表不支持的目标结构必须提前拒绝(
        string definition)
    {
        EtlMySqlCreateDefinition
            .HasUnsupportedTemporaryCloneShape(
                definition,
                backslashEscapes: true)
            .ShouldBeTrue();
    }

    [Fact]
    public void MySql临时表结构判断不得解释引用文本()
    {
        const string definition = """
            CREATE TABLE `target` (
              `note` varchar(200) DEFAULT
                'FULLTEXT TABLESPACE ROW_FORMAT=COMPRESSED KEY_BLOCK_SIZE=8'
            ) ENGINE=InnoDB
            COMMENT='TABLESPACE FULLTEXT'
            """;

        EtlMySqlCreateDefinition
            .HasUnsupportedTemporaryCloneShape(
                definition,
                backslashEscapes: true)
            .ShouldBeFalse();
    }

    [Fact]
    public void MySql反引号标识符不得套用字符串反斜杠转义规则()
    {
        const string definition = """
            CREATE TABLE `target` (
              `path\``name` varchar(20)
            ) ENGINE=InnoDB AUTO_INCREMENT=123
            """;

        var normalized =
            EtlMySqlCreateDefinition.NormalizeForFingerprint(
                definition,
                backslashEscapes: true);

        normalized.ShouldContain("`path\\``name`");
        normalized.ShouldContain("AUTO_INCREMENT=0");
    }

    private static void ShouldRejectProjection(
        string[] source,
        EtlTargetColumnContract.Column[] target,
        bool truncate)
    {
        var exception = Should.Throw<BusinessException>(() =>
            EtlTargetColumnContract.ValidateProjection(
                source,
                target,
                truncate));
        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineExecutionInputInvalid);
    }
}
