using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 源查询词法守卫测试：锁定 SQL Server/MySQL 单语句只读边界，
/// 同时证明字符串、注释和引用标识符中的敏感文本不会被误判。
/// </summary>
public class EtlSourceQueryGuardTests
{
    [Theory]
    [InlineData("sqlserver", "SELECT id, name FROM dbo.users")]
    [InlineData("mssql", "  /* report */ SELECT 1; -- trailing comment")]
    [InlineData("mysql", "select id from users")]
    [InlineData("mariadb", "# report\nSELECT 1; # trailing comment")]
    [InlineData("sqlserver", "SELECT '; DELETE FROM t' AS [drop;table], [INTO], [UPDLOCK]")]
    [InlineData("mysql", "SELECT '; DELETE FROM t' AS `drop;table`, `INTO`, `SLEEP`")]
    [InlineData("sqlserver", "SELECT 'it''s safe', \"OPENQUERY\" FROM [safe;table]")]
    [InlineData("mysql", "SELECT 'it''s safe', \"OUTFILE\" FROM `safe;table`")]
    [InlineData("sqlserver", "SELECT col/* ordinary comment */ FROM dbo.t")]
    [InlineData("mysql", "SELECT col/* ordinary comment */ FROM t")]
    [InlineData("sqlserver", "SELECT REPLACE(name, '-', '') FROM dbo.users")]
    [InlineData("sqlserver", "SELECT category, COUNT(*) FROM dbo.t GROUP BY GROUPING SETS ((category), ())")]
    [InlineData("mysql", "SELECT REPLACE(name, '-', '') FROM users")]
    [InlineData("mysql", "SELECT INSERT(code, 1, 0, 'P-') FROM items")]
    [InlineData("postgresql", "SELECT id, name FROM public.orders")]
    public void 普通单条查询_字符串和引用标识符中的敏感文本_允许(
        string provider,
        string query)
        => Should.NotThrow(() => EtlSourceQueryGuard.Validate(query, provider));

    [Theory]
    [InlineData("sqlserver", "WITH c AS (SELECT 1 AS id) SELECT id FROM c")]
    [InlineData("sqlserver", "WITH c(id) AS (SELECT 1), d AS (SELECT id FROM c) SELECT id FROM d;")]
    [InlineData("mysql", "WITH c AS (SELECT 1 AS id) SELECT id FROM c")]
    [InlineData("mysql", "WITH RECURSIVE c AS (SELECT 1 UNION ALL SELECT 2) SELECT * FROM c")]
    public void 以SELECT收口的CTE_允许(string provider, string query)
        => Should.NotThrow(() => EtlSourceQueryGuard.Validate(query, provider));

    [Theory]
    [InlineData("sqlserver", "SELECT 1; DELETE FROM dbo.t")]
    [InlineData("sqlserver", "SELECT 1; SELECT 2")]
    [InlineData("sqlserver", "SELECT 1 DELETE FROM dbo.t")]
    [InlineData("sqlserver", "SELECT 1 SELECT 2")]
    [InlineData("sqlserver", "SELECT 1\nSHUTDOWN WITH NOWAIT")]
    [InlineData("sqlserver", "SELECT 1\nDISABLE TRIGGER ALL ON DATABASE")]
    [InlineData("sqlserver", "SELECT 1\nUPDATETEXT dbo.t.body 0x1234 0 0 'x'")]
    [InlineData("sqlserver", "SELECT 1\nGO\nSELECT 2")]
    [InlineData("sqlserver", ";SELECT 1")]
    [InlineData("sqlserver", "SELECT 1;;")]
    [InlineData("mysql", "SELECT 1; DELETE FROM t")]
    [InlineData("mysql", "SELECT 1; SELECT 2")]
    [InlineData("mysql", "SELECT 1 DELETE FROM t")]
    [InlineData("mysql", "SELECT 1 SELECT 2")]
    [InlineData("mysql", ";SELECT 1")]
    [InlineData("mysql", "SELECT 1;;")]
    public void 批次或多语句_拒绝(string provider, string query)
        => ShouldReject(provider, query);

    [Theory]
    [InlineData("sqlserver", "SELECT 1 AS id UNION ALL SELECT 2")]
    [InlineData("sqlserver", "SELECT 1 AS id INTERSECT SELECT 1")]
    [InlineData("mysql", "SELECT 1 AS id UNION SELECT 2")]
    public void 集合运算的后续SELECT_仍属于同一查询表达式_允许(
        string provider,
        string query)
        => Should.NotThrow(() => EtlSourceQueryGuard.Validate(query, provider));

    [Theory]
    [InlineData("sqlserver", "SELECT id INTO dbo.copied_users FROM dbo.users")]
    [InlineData("mysql", "SELECT id FROM users INTO OUTFILE '/tmp/users.txt'")]
    [InlineData("mysql", "SELECT id FROM users INTO DUMPFILE '/tmp/users.bin'")]
    [InlineData("mysql", "SELECT id INTO @captured FROM users")]
    public void SELECT内置写入或会话赋值_拒绝(string provider, string query)
        => ShouldReject(provider, query);

    [Theory]
    [InlineData("sqlserver", "WITH c AS (SELECT id FROM dbo.t) DELETE FROM c")]
    [InlineData("sqlserver", "WITH c AS (SELECT id FROM dbo.t) UPDATE c SET id = 2")]
    [InlineData("sqlserver", "WITH c AS (SELECT id FROM dbo.t) MERGE dbo.x USING c ON 1 = 0 WHEN NOT MATCHED THEN INSERT DEFAULT VALUES")]
    [InlineData("mysql", "WITH c AS (SELECT id FROM t) DELETE FROM t WHERE id IN (SELECT id FROM c)")]
    [InlineData("mysql", "WITH c AS (SELECT id FROM t) UPDATE t SET id = 2")]
    public void CTE主语句不是SELECT_拒绝(string provider, string query)
        => ShouldReject(provider, query);

    [Theory]
    [InlineData("mysql", "SELECT 1 /*! INTO OUTFILE '/tmp/pwn' */")]
    [InlineData("mariadb", "SELECT 1 /*M! INTO OUTFILE '/tmp/pwn' */")]
    [InlineData("mysql", "SELECT /*+ MAX_EXECUTION_TIME(999999) */ 1")]
    [InlineData("mysql", "SELECT 1--x INTO OUTFILE '/tmp/pwn'")]
    [InlineData("mysql", "SELECT 1--\u00A0INTO OUTFILE '/tmp/pwn'")]
    public void MySql可执行注释和注释规则差异_拒绝(string provider, string query)
        => ShouldReject(provider, query);

    [Theory]
    [InlineData("sqlserver", "SELECT * FROM dbo.t WITH (UPDLOCK, HOLDLOCK)")]
    [InlineData("sqlserver", "SELECT * FROM dbo.t WITH (NOLOCK)")]
    [InlineData("sqlserver", "SELECT * FROM dbo.t WITH (FORCESEEK(ix_t(id)))")]
    [InlineData("sqlserver", "SELECT * FROM dbo.t WITH (INDEX(ix_t))")]
    [InlineData("sqlserver", "SELECT * FROM OPENQUERY(linked, 'EXEC dbo.mutate')")]
    [InlineData("sqlserver", "SELECT * FROM OPENROWSET(BULK 'secret.txt', SINGLE_CLOB) AS x")]
    [InlineData("sqlserver", "SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Data Source=server;Integrated Security=SSPI').db.dbo.t")]
    [InlineData("sqlserver", "SELECT NEXT VALUE FOR dbo.sequence_name")]
    [InlineData("sqlserver", "SELECT * FROM dbo.t WITH (ROWLOCK)")]
    [InlineData("sqlserver", "SELECT * FROM dbo.t WITH (REPEATABLEREAD)")]
    [InlineData("sqlserver", "SELECT * FROM linked_server.database.dbo.users")]
    [InlineData("sqlserver", "SELECT * FROM linked_server.database.dbo.remote_function()")]
    [InlineData("sqlserver", "SELECT * FROM OPENXML(@handle, '/root/item')")]
    [InlineData("mysql", "SELECT * FROM t FOR UPDATE")]
    [InlineData("mysql", "SELECT * FROM t FOR SHARE")]
    [InlineData("mysql", "SELECT * FROM t LOCK IN SHARE MODE")]
    [InlineData("mysql", "SELECT SLEEP(60)")]
    [InlineData("mysql", "SELECT BENCHMARK(1000000, SHA2('x', 512))")]
    [InlineData("mysql", "SELECT GET_LOCK('etl', 60)")]
    [InlineData("mysql", "SELECT LOAD_FILE('/etc/passwd')")]
    [InlineData("mysql", "SELECT NEXTVAL(sequence_name)")]
    [InlineData("mysql", "SELECT SETVAL(sequence_name, 42)")]
    [InlineData("mysql", "SELECT SERVICE_GET_WRITE_LOCKS('etl', 60)")]
    [InlineData("mysql", "SELECT WAIT_FOR_EXECUTED_GTID_SET('x', 60)")]
    [InlineData("mysql", "SELECT `SLEEP`(60)")]
    [InlineData("mysql", "SELECT `GET_LOCK`('etl', 60)")]
    [InlineData("mysql", "SELECT `LOAD_FILE`('/etc/passwd')")]
    [InlineData("mysql", "SELECT \"SLEEP\"(60)")]
    [InlineData("mysql", "SELECT `NEXTVAL`(sequence_name)")]
    [InlineData("mysql", "SELECT HIGH_PRIORITY * FROM t")]
    public void 显式锁_外部访问_序列和DoS构造_拒绝(string provider, string query)
        => ShouldReject(provider, query);

    [Theory]
    [InlineData("sqlserver", "SELECT @value")]
    [InlineData("sqlserver", "SELECT @@VERSION")]
    [InlineData("mysql", "SELECT @value := 1")]
    [InlineData("mysql", "SELECT ?")]
    [InlineData("mysql", "SELECT :value")]
    public void 当前无参数契约_参数和用户变量_拒绝(string provider, string query)
        => ShouldReject(provider, query);

    [Theory]
    [InlineData("sqlserver", "SELECT 'unterminated")]
    [InlineData("sqlserver", "SELECT [unterminated")]
    [InlineData("sqlserver", "SELECT 1 /* unterminated")]
    [InlineData("sqlserver", "SELECT (1")]
    [InlineData("mysql", "SELECT 'unterminated")]
    [InlineData("mysql", "SELECT `unterminated")]
    [InlineData("mysql", "SELECT 1 /* unterminated")]
    [InlineData("mysql", "SELECT (1")]
    [InlineData("mysql", "SELECT 'a\\'; DELETE FROM t")]
    public void 未闭合或MySql转义模式有歧义_拒绝(string provider, string query)
        => ShouldReject(provider, query);

    [Fact]
    public void SqlServer嵌套块注释_按方言允许()
        => Should.NotThrow(() =>
            EtlSourceQueryGuard.Validate(
                "SELECT 1 /* outer /* inner */ outer */",
                "sqlserver"));

    [Fact]
    public void MySql嵌套块注释_按方言拒绝()
        => ShouldReject(
            "mysql",
            "SELECT 1 /* outer /* inner */ outer */");

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    public void 注释嵌入关键字_拒绝(string provider)
        => ShouldReject(provider, "SELECT 1 IN/**/TO copied");

    [Theory]
    [InlineData("")]
    [InlineData("sqlite")]
    [InlineData("oracle")]
    public void 未支持provider_拒绝(string provider)
        => ShouldReject(provider, "SELECT 1");

    [Fact]
    public void 超长查询_在解析前拒绝()
        => ShouldReject(
            "sqlserver",
            $"SELECT 1 /* {new string('x', 64 * 1024)} */");

    [Fact]
    public void 拒绝结果_只含稳定错误码_不携带SQL或判定细节()
    {
        const string secret = "P0_SQL_SECRET_SENTINEL";

        var exception = Should.Throw<BusinessException>(() =>
            EtlSourceQueryGuard.Validate(
                $"SELECT 1; DELETE FROM secret_table /* {secret} */",
                "sqlserver"));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        exception.Message.ShouldNotContain(secret);
        exception.Data.Keys.Cast<object>().ShouldBeEmpty();
    }

    private static void ShouldReject(string provider, string query)
    {
        var exception = Should.Throw<BusinessException>(
            () => EtlSourceQueryGuard.Validate(query, provider));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
    }
}
