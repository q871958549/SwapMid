using System;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// 真 ADO.NET reader + 真 SQLite 事务验证 ETL 搬运核心。每个测试使用两套独立共享内存库，
/// 不复用 ABP EF 测试宿主的单连接，避免把 SQLite 单连接竞态带进本组。
/// </summary>
public class EtlPipelineRunnerTests
{
    [Fact]
    public async Task 成功搬运1001行_跨批次后一次提交并替换旧数据()
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(1_001);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            """);
        var runner = CreateRunner(databases);

        var result = await runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform("SELECT probe_read(id) AS id, value FROM source_rows ORDER BY id"));

        result.Rows.ShouldBe(1_001);
        result.TargetTruncated.ShouldBeTrue();
        databases.ProbeReads.ShouldBe(1_001);
        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(1_001);
        (await databases.TargetScalarAsync<long>("SELECT MIN(id) FROM target_rows")).ShouldBe(1);
        (await databases.TargetScalarAsync<long>("SELECT MAX(id) FROM target_rows")).ShouldBe(1_001);
    }

    [Theory]
    [InlineData("truncate")]
    [InlineData("append")]
    public async Task 第三批写入失败_DELETE或先前INSERT全部回滚且连接锁释放(
        string mode)
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(1_001);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            CREATE TRIGGER reject_1001
            BEFORE INSERT ON target_rows
            WHEN NEW.id = 1001
            BEGIN
                SELECT RAISE(ABORT, 'injected target failure');
            END;
            """);
        var runner = CreateRunner(databases);

        await Should.ThrowAsync<SqliteException>(() => runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform("SELECT id, value FROM source_rows ORDER BY id", mode)));

        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(1);
        (await databases.TargetScalarAsync<long>("SELECT id FROM target_rows")).ShouldBe(-1);

        // 若 reader / transaction / connection 未释放，这个第二连接写会触发 database is locked。
        await databases.ExecuteTargetAsync(
            "INSERT INTO target_rows (id, value) VALUES (-2, 'after-rollback');");
        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(2);
    }

    [Fact]
    public async Task 第50001行对调用方立即拒绝_仅容许驱动预取一行且目标恢复执行前快照()
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(EtlPipelineRunner.MaxRows + 10_000);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            """);
        var runner = CreateRunner(databases);

        var exception = await Should.ThrowAsync<BusinessException>(() => runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform("SELECT probe_read(id) AS id, value FROM source_rows")));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        // SQLite 对含 ORDER BY 的结果会在 ReadAsync 返回前预取并求值下一行；应用循环
        // 没有发起第 50,002 次 ReadAsync，但 provider 内部最多可能多求值一行。
        databases.ProbeReads.ShouldBeInRange(
            EtlPipelineRunner.MaxRows + 1,
            EtlPipelineRunner.MaxRows + 2);
        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(1);
        (await databases.TargetScalarAsync<long>("SELECT id FROM target_rows")).ShouldBe(-1);

        await databases.ExecuteTargetAsync(
            "INSERT INTO target_rows (id, value) VALUES (-2, 'after-limit');");
    }

    [Fact]
    public async Task 零行truncate_仍在事务中提交DELETE()
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(0);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            """);
        var runner = CreateRunner(databases);

        var result = await runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform("SELECT id, value FROM source_rows ORDER BY id"));

        result.Rows.ShouldBe(0);
        result.TargetTruncated.ShouldBeTrue();
        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(0);
    }

    [Fact]
    public async Task 旧目标超过同步删除上限_在DELETE前拒绝且保留原表()
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(1);
        await databases.ExecuteTargetAsync(
            $"""
             CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
             WITH RECURSIVE numbers(id) AS
             (
                 SELECT 1
                 UNION ALL
                 SELECT id + 1 FROM numbers
                 WHERE id <= {EtlPipelineRunner.MaxExistingTargetRows}
             )
             INSERT INTO target_rows (id, value)
             SELECT id, 'old-' || id FROM numbers;
             """);
        var runner = CreateRunner(databases);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            runner.RunAsync(
                CreateDataSource("source"),
                CreateDataSource("target"),
                Transform("SELECT id, value FROM source_rows")));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        (await databases.TargetScalarAsync<long>(
            "SELECT COUNT(*) FROM target_rows"))
            .ShouldBe(EtlPipelineRunner.MaxExistingTargetRows + 1L);
    }

    [Fact]
    public async Task 重复源列名_在目标DELETE前拒绝()
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(1);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            """);
        var runner = CreateRunner(databases);

        var exception = await Should.ThrowAsync<BusinessException>(() => runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform("SELECT id AS duplicate_name, value AS duplicate_name FROM source_rows")));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(1);
        (await databases.TargetScalarAsync<long>("SELECT id FROM target_rows")).ShouldBe(-1);
    }

    [Fact]
    public async Task 同一数据库双连接truncate_先关闭reader再写正式表不会自阻塞()
    {
        await using var databases = await SqliteEtlDatabases.CreateSharedAsync();
        await databases.SeedSourceAsync(3);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            """);
        var runner = CreateRunner(databases);

        var result = await runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform("SELECT id, value FROM source_rows ORDER BY id"));

        result.Rows.ShouldBe(3);
        result.TargetTruncated.ShouldBeTrue();
        databases.CreatedConnections.ShouldBe(2);
        (await databases.TargetScalarAsync<long>(
            "SELECT COUNT(*) FROM target_rows")).ShouldBe(3);
        (await databases.TargetScalarAsync<long>(
            "SELECT MIN(id) FROM target_rows")).ShouldBe(1);
    }

    [Fact]
    public async Task 目标静默忽略一行_整次回滚且不得报告假成功()
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(501);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            CREATE TRIGGER ignore_501
            BEFORE INSERT ON target_rows
            WHEN NEW.id = 501
            BEGIN
                SELECT RAISE(IGNORE);
            END;
            """);
        var runner = CreateRunner(databases);

        var exception = await Should.ThrowAsync<BusinessException>(() => runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform("SELECT id, value FROM source_rows ORDER BY id", "append")));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(1);
        (await databases.TargetScalarAsync<long>("SELECT id FROM target_rows")).ShouldBe(-1);
    }

    [Fact]
    public async Task 单个文本值超过4MiB_有界拒绝并回滚目标()
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(1);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            """);
        var runner = CreateRunner(databases);
        var oversizedCharacters =
            EtlPipelineRunner.MaxCellPayloadBytes / sizeof(char) + 1;

        var exception = await Should.ThrowAsync<BusinessException>(() => runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform(
                $"SELECT id, printf('%*s', {oversizedCharacters}, 'x') AS value FROM source_rows")));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(1);
        (await databases.TargetScalarAsync<long>("SELECT id FROM target_rows")).ShouldBe(-1);
    }

    [Fact]
    public async Task 源读取中取消_目标回滚且连接可立即复用()
    {
        await using var databases = await SqliteEtlDatabases.CreateAsync();
        await databases.SeedSourceAsync(1_001);
        await databases.ExecuteTargetAsync(
            """
            CREATE TABLE target_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);
            INSERT INTO target_rows (id, value) VALUES (-1, 'sentinel');
            """);
        using var cancellation = new CancellationTokenSource();
        databases.CancelSourceAt(cancellation, 1001);
        var runner = CreateRunner(databases);

        await Should.ThrowAsync<OperationCanceledException>(() => runner.RunAsync(
            CreateDataSource("source"),
            CreateDataSource("target"),
            Transform("SELECT probe_read(id) AS id, value FROM source_rows"),
            cancellation.Token));

        (await databases.TargetScalarAsync<long>("SELECT COUNT(*) FROM target_rows")).ShouldBe(1);
        (await databases.TargetScalarAsync<long>("SELECT id FROM target_rows")).ShouldBe(-1);
        await databases.ExecuteTargetAsync(
            "INSERT INTO target_rows (id, value) VALUES (-2, 'after-cancel');");
    }

    private static EtlPipelineRunner CreateRunner(
        IEtlRelationalConnectionFactory connectionFactory)
        => new(NullLogger<EtlPipelineRunner>.Instance, connectionFactory);

    private static DataSource CreateDataSource(string database)
        => new(
            Guid.NewGuid(),
            database,
            DataSourceType.RelationalDatabase,
            $$"""{"provider":"sqlserver","database":"{{database}}"}""");

    private static string Transform(string sourceQuery, string mode = "truncate")
        => JsonSerializer.Serialize(new
        {
            sourceQuery,
            targetTable = "target_rows",
            mode,
        });

    private sealed class SqliteEtlDatabases
        : IEtlRelationalConnectionFactory, IAsyncDisposable
    {
        private readonly string _sourceConnectionString;
        private readonly string _targetConnectionString;
        private readonly SqliteConnection _sourceAnchor;
        private readonly SqliteConnection _targetAnchor;
        private int _probeReads;
        private int _createdConnections;
        private long _cancelAt;
        private CancellationTokenSource? _cancellationSource;

        public int ProbeReads => Volatile.Read(ref _probeReads);
        public int CreatedConnections => Volatile.Read(ref _createdConnections);

        private SqliteEtlDatabases(
            string sourceConnectionString,
            string targetConnectionString,
            SqliteConnection sourceAnchor,
            SqliteConnection targetAnchor)
        {
            _sourceConnectionString = sourceConnectionString;
            _targetConnectionString = targetConnectionString;
            _sourceAnchor = sourceAnchor;
            _targetAnchor = targetAnchor;
        }

        public static async Task<SqliteEtlDatabases> CreateAsync()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var sourceConnectionString =
                $"Data Source=etl-source-{suffix};Mode=Memory;Cache=Shared;Default Timeout=1";
            var targetConnectionString =
                $"Data Source=etl-target-{suffix};Mode=Memory;Cache=Shared;Default Timeout=1";
            var sourceAnchor = new SqliteConnection(sourceConnectionString);
            var targetAnchor = new SqliteConnection(targetConnectionString);
            await sourceAnchor.OpenAsync();
            await targetAnchor.OpenAsync();
            return new SqliteEtlDatabases(
                sourceConnectionString,
                targetConnectionString,
                sourceAnchor,
                targetAnchor);
        }

        public static async Task<SqliteEtlDatabases> CreateSharedAsync()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var sharedConnectionString =
                $"Data Source=etl-shared-{suffix};Mode=Memory;Cache=Shared;Default Timeout=1";
            var sourceAnchor = new SqliteConnection(sharedConnectionString);
            var targetAnchor = new SqliteConnection(sharedConnectionString);
            await sourceAnchor.OpenAsync();
            await targetAnchor.OpenAsync();
            return new SqliteEtlDatabases(
                sharedConnectionString,
                sharedConnectionString,
                sourceAnchor,
                targetAnchor);
        }

        public DbConnection Create(string configJson, int connectTimeoutSeconds)
        {
            Interlocked.Increment(ref _createdConnections);
            using var json = JsonDocument.Parse(configJson);
            var database = json.RootElement.GetProperty("database").GetString();
            var connection = new SqliteConnection(
                database == "source"
                    ? _sourceConnectionString
                    : _targetConnectionString);
            if (database == "source")
            {
                connection.CreateFunction<long, long>(
                    "probe_read",
                    value =>
                    {
                        Interlocked.Increment(ref _probeReads);
                        if (value == Volatile.Read(ref _cancelAt))
                        {
                            Volatile.Read(ref _cancellationSource)?.Cancel();
                        }

                        return value;
                    });
            }

            return connection;
        }

        public void CancelSourceAt(
            CancellationTokenSource cancellationSource,
            long sourceId)
        {
            Volatile.Write(ref _cancellationSource, cancellationSource);
            Volatile.Write(ref _cancelAt, sourceId);
        }

        public async Task SeedSourceAsync(int count)
        {
            await ExecuteAsync(
                _sourceAnchor,
                "CREATE TABLE source_rows (id INTEGER NOT NULL PRIMARY KEY, value TEXT NULL);");
            if (count == 0)
            {
                return;
            }

            await ExecuteAsync(
                _sourceAnchor,
                $"""
                 WITH RECURSIVE numbers(id) AS
                 (
                     SELECT 1
                     UNION ALL
                     SELECT id + 1 FROM numbers WHERE id < {count}
                 )
                 INSERT INTO source_rows (id, value)
                 SELECT id, 'value-' || id FROM numbers;
                 """);
        }

        public Task ExecuteTargetAsync(string sql)
            => ExecuteAsync(_targetAnchor, sql);

        public async Task<T> TargetScalarAsync<T>(string sql)
        {
            await using var command = _targetAnchor.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T));
        }

        public async ValueTask DisposeAsync()
        {
            await _sourceAnchor.DisposeAsync();
            await _targetAnchor.DisposeAsync();
        }

        private static async Task ExecuteAsync(SqliteConnection connection, string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
