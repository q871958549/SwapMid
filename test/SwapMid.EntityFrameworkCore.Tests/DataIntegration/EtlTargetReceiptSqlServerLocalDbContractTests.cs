using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Shouldly;
using Volo.Abp;
using Xunit;
using Xunit.Sdk;

namespace SwapMid.DataIntegration;

/// <summary>
/// Receipt v1 的真实 SQL Server provider 合约。默认跳过；Windows/Gitea Runner 设置
/// RUN_SQLSERVER_LOCALDB_CONTRACT_TESTS=1 后，以临时库证明精确 DDL 与事务行为探针。
/// </summary>
public sealed class EtlTargetReceiptSqlServerLocalDbContractTests
{
    private const string RuntimePassword = "LocalDbContract!9xQ2z#7K";

    [SqlServerLocalDbContractFact]
    [Trait("Category", "SqlServerContract")]
    public async Task 激活必须证明精确结构约束拒绝与回滚无残留()
    {
        var database = $"SwapMidEtlReceiptContract_{Guid.NewGuid():N}";
        var runtimeLogin = $"swapmid_etl_runtime_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;
        var databaseConnectionString = new SqlConnectionStringBuilder(
            masterConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;

        await CreateDatabaseAsync(masterConnectionString, database);
        try
        {
            await ExecuteAsync(
                databaseConnectionString,
                ReadRepositoryFile(
                    "scripts/data-integration/target-receipt-v1/sqlserver.sql"));
            await ExecuteAsync(
                databaseConnectionString,
                "CREATE TABLE dbo.target_rows (id int NOT NULL);");
            var runtimeConnectionString =
                await ConfigureRuntimePrincipalAsync(
                    masterConnectionString,
                    databaseConnectionString,
                    runtimeLogin);

            var store = new EtlTargetReceiptStore(
                new LocalDbConnectionFactory(runtimeConnectionString));
            var activation = await store.ProbeActivationAsync(
                CreateDataSource(database),
                EtlTransformConfig.Parse(JsonSerializer.Serialize(new
                {
                    sourceQuery = "SELECT 1 AS id",
                    targetTable = "dbo.target_rows",
                    mode = "append-receipt-v1",
                })),
                default);

            activation.ResourceInstanceId.ShouldNotBe(Guid.Empty);
            activation.Provider.ShouldBe("sqlserver");
            activation.CanonicalTargetTable.ShouldBe("dbo.target_rows");
            activation.TargetCoordinationIdentityHash.Length.ShouldBe(
                PipelineExecutionConsts.Sha256HexLength);
            activation.TargetCoordinationIdentityHash.All(character =>
                    character is >= '0' and <= '9' or >= 'A' and <= 'F')
                .ShouldBeTrue();
            var replayActivation = await store.ProbeActivationAsync(
                CreateDataSource(database),
                EtlTransformConfig.Parse(JsonSerializer.Serialize(new
                {
                    sourceQuery = "SELECT 1 AS id",
                    targetTable = "dbo.target_rows",
                    mode = "append-receipt-v1",
                })),
                default);
            replayActivation.ResourceInstanceId.ShouldBe(
                activation.ResourceInstanceId);
            replayActivation.TargetCoordinationIdentityHash.ShouldBe(
                activation.TargetCoordinationIdentityHash,
                "同一 SQL Server 实例和 database 的 coordination identity 必须稳定");
            (await QueryLongAsync(
                databaseConnectionString,
                "SELECT COUNT_BIG(*) FROM swapmid_etl.ExecutionReceiptV1"))
                .ShouldBe(0);

            await ExecuteAsync(
                databaseConnectionString,
                """
                CREATE TRIGGER swapmid_etl.TR_ExecutionReceiptV1_drift
                ON swapmid_etl.ExecutionReceiptV1
                AFTER INSERT
                AS RETURN;
                """);
            var drift = await Should.ThrowAsync<BusinessException>(() =>
                store.ProbeActivationAsync(
                    CreateDataSource(database),
                    EtlTransformConfig.Parse(JsonSerializer.Serialize(new
                    {
                        sourceQuery = "SELECT 1 AS id",
                        targetTable = "dbo.target_rows",
                        mode = "append-receipt-v1",
                    })),
                    default));
            drift.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(masterConnectionString, database);
            }
            finally
            {
                await DropLoginAsync(masterConnectionString, runtimeLogin);
            }
        }
    }

    [SqlServerLocalDbContractFact]
    [Trait("Category", "SqlServerContract")]
    public async Task 激活必须在行为探针前拒绝拥有Control的运行账号()
    {
        var database = $"SwapMidEtlReceiptPermission_{Guid.NewGuid():N}";
        var runtimeLogin = $"swapmid_etl_runtime_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;
        var databaseConnectionString = new SqlConnectionStringBuilder(
            masterConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;

        await CreateDatabaseAsync(masterConnectionString, database);
        try
        {
            await ExecuteAsync(
                databaseConnectionString,
                ReadRepositoryFile(
                    "scripts/data-integration/target-receipt-v1/sqlserver.sql"));
            await ExecuteAsync(
                databaseConnectionString,
                """
                CREATE TABLE dbo.target_rows (id int NOT NULL);
                INSERT INTO dbo.target_rows (id) VALUES (7);
                """);
            var runtimeConnectionString =
                await ConfigureRuntimePrincipalAsync(
                    masterConnectionString,
                    databaseConnectionString,
                    runtimeLogin,
                    grantProhibitedControl: true);
            var store = new EtlTargetReceiptStore(
                new LocalDbConnectionFactory(runtimeConnectionString));

            var exception = await Should.ThrowAsync<BusinessException>(() =>
                store.ProbeActivationAsync(
                    CreateDataSource(database),
                    EtlTransformConfig.Parse(JsonSerializer.Serialize(new
                    {
                        sourceQuery = "SELECT 1 AS id",
                        targetTable = "dbo.target_rows",
                        mode = "append-receipt-v1",
                    })),
                    default));
            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid);
            (await QueryLongAsync(
                    databaseConnectionString,
                    "SELECT COUNT_BIG(*) FROM swapmid_etl.ExecutionReceiptV1"))
                .ShouldBe(0,
                    "有效权限矩阵必须在 INSERT/SELECT/rollback 行为探针前拒绝宽权限账号");
            (await QueryLongAsync(
                    databaseConnectionString,
                    "SELECT COUNT_BIG(*) FROM dbo.target_rows WHERE id = 7"))
                .ShouldBe(1);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(masterConnectionString, database);
            }
            finally
            {
                await DropLoginAsync(masterConnectionString, runtimeLogin);
            }
        }
    }

    [SqlServerLocalDbContractFact]
    [Trait("Category", "SqlServerContract")]
    public async Task 激活必须拒绝可覆盖表级Deny的列级Update授权()
    {
        var database = $"SwapMidEtlReceiptColumnPermission_{Guid.NewGuid():N}";
        var runtimeLogin = $"swapmid_etl_runtime_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;
        var databaseConnectionString = new SqlConnectionStringBuilder(
            masterConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;

        await CreateDatabaseAsync(masterConnectionString, database);
        try
        {
            await ExecuteAsync(
                databaseConnectionString,
                ReadRepositoryFile(
                    "scripts/data-integration/target-receipt-v1/sqlserver.sql"));
            await ExecuteAsync(
                databaseConnectionString,
                "CREATE TABLE dbo.target_rows (id int NOT NULL);");
            var runtimeConnectionString =
                await ConfigureRuntimePrincipalAsync(
                    masterConnectionString,
                    databaseConnectionString,
                    runtimeLogin,
                    grantColumnUpdate: true);
            var store = new EtlTargetReceiptStore(
                new LocalDbConnectionFactory(runtimeConnectionString));

            var exception = await Should.ThrowAsync<BusinessException>(() =>
                store.ProbeActivationAsync(
                    CreateDataSource(database),
                    EtlTransformConfig.Parse(JsonSerializer.Serialize(new
                    {
                        sourceQuery = "SELECT 1 AS id",
                        targetTable = "dbo.target_rows",
                        mode = "append-receipt-v1",
                    })),
                    default));
            exception.Code.ShouldBe(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid);
            (await QueryLongAsync(
                    databaseConnectionString,
                    "SELECT COUNT_BIG(*) FROM swapmid_etl.ExecutionReceiptV1"))
                .ShouldBe(0);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(masterConnectionString, database);
            }
            finally
            {
                await DropLoginAsync(masterConnectionString, runtimeLogin);
            }
        }
    }

    [SqlServerLocalDbContractFact]
    [Trait("Category", "SqlServerContract")]
    public async Task 伪造CoordinationHash必须在回执读写前拒绝且不触碰业务表()
    {
        var database = $"SwapMidEtlReceiptIdentity_{Guid.NewGuid():N}";
        var runtimeLogin = $"swapmid_etl_runtime_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;
        var databaseConnectionString = new SqlConnectionStringBuilder(
            masterConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;

        await CreateDatabaseAsync(masterConnectionString, database);
        try
        {
            await ExecuteAsync(
                databaseConnectionString,
                ReadRepositoryFile(
                    "scripts/data-integration/target-receipt-v1/sqlserver.sql"));
            await ExecuteAsync(
                databaseConnectionString,
                """
                CREATE TABLE dbo.target_rows (id int NOT NULL);
                INSERT INTO dbo.target_rows (id) VALUES (7);
                """);
            var runtimeConnectionString =
                await ConfigureRuntimePrincipalAsync(
                    masterConnectionString,
                    databaseConnectionString,
                    runtimeLogin);

            var target = CreateDataSource(database);
            var store = new EtlTargetReceiptStore(
                new LocalDbConnectionFactory(runtimeConnectionString));
            var activation = await store.ProbeActivationAsync(
                target,
                EtlTransformConfig.Parse(JsonSerializer.Serialize(new
                {
                    sourceQuery = "SELECT 1 AS id",
                    targetTable = "dbo.target_rows",
                    mode = "append-receipt-v1",
                })),
                default);
            var evidence = CreateEvidence(activation);
            var validContext = CreateExecutionContext(
                activation,
                evidence);
            var forgedHash = Hash('0');
            if (string.Equals(
                    forgedHash,
                    activation.TargetCoordinationIdentityHash,
                    StringComparison.Ordinal))
            {
                forgedHash = Hash('1');
            }
            var forgedContext = validContext with
            {
                TargetCoordinationIdentityHash = forgedHash,
            };

            using var deadline = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));
            await using var blockerConnection =
                new SqlConnection(databaseConnectionString);
            await blockerConnection.OpenAsync(deadline.Token);
            await using var blockerTransaction =
                await blockerConnection.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    deadline.Token);
            await using (var lockCommand = blockerConnection.CreateCommand())
            {
                lockCommand.Transaction =
                    (SqlTransaction)blockerTransaction;
                lockCommand.CommandText = """
                    SELECT COUNT_BIG(*)
                    FROM swapmid_etl.ExecutionReceiptV1
                        WITH (TABLOCKX, HOLDLOCK);
                    """;
                await lockCommand.ExecuteScalarAsync(deadline.Token);
            }

            using (var findTimeout =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       deadline.Token))
            {
                findTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var exception = await Should.ThrowAsync<BusinessException>(
                    () => store.FindAsync(
                        target,
                        forgedContext,
                        findTimeout.Token));
                exception.Code.ShouldBe(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionInputInvalid);
            }

            await using (var insertConnection =
                         new SqlConnection(databaseConnectionString))
            {
                await insertConnection.OpenAsync(deadline.Token);
                await using var insertTransaction =
                    await insertConnection.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted,
                        deadline.Token);
                using var insertTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        deadline.Token);
                insertTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var exception = await Should.ThrowAsync<BusinessException>(
                    () => store.InsertOrReadAsync(
                        insertConnection,
                        insertTransaction,
                        new EtlTargetReceiptProviderContext(
                            activation.Provider,
                            database,
                            forgedHash),
                        evidence,
                        commandTimeoutSeconds: 60,
                        insertTimeout.Token));
                exception.Code.ShouldBe(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionInputInvalid);
                await insertTransaction.RollbackAsync(deadline.Token);
            }

            using (var fenceTimeout =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       deadline.Token))
            {
                fenceTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var exception = await Should.ThrowAsync<BusinessException>(
                    () => store.AcquireReadFenceAsync(
                        target,
                        forgedContext,
                        fenceTimeout.Token));
                exception.Code.ShouldBe(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionInputInvalid);
            }

            (await QueryLongAsync(
                    blockerConnection,
                    blockerTransaction,
                    "SELECT COUNT_BIG(*) FROM swapmid_etl.ExecutionReceiptV1;",
                    deadline.Token))
                .ShouldBe(0,
                    "伪造 identity 不得写入任何 receipt");
            await blockerTransaction.RollbackAsync(deadline.Token);

            (await QueryLongAsync(
                    databaseConnectionString,
                    "SELECT COUNT_BIG(*) FROM swapmid_etl.ExecutionReceiptV1"))
                .ShouldBe(0);
            (await QueryLongAsync(
                    databaseConnectionString,
                    "SELECT COUNT_BIG(*) FROM dbo.target_rows WHERE id = 7"))
                .ShouldBe(1,
                    "Find/Insert/read-fence identity 拒绝都不得修改业务表");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(masterConnectionString, database);
            }
            finally
            {
                await DropLoginAsync(masterConnectionString, runtimeLogin);
            }
        }
    }

    [SqlServerLocalDbContractFact]
    [Trait("Category", "SqlServerContract")]
    public async Task InsertOrRead必须拒绝错误连接事务归属与非SqlServerProvider()
    {
        var database = $"SwapMidEtlReceiptBoundary_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;
        var databaseConnectionString = new SqlConnectionStringBuilder(
            masterConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;

        await CreateDatabaseAsync(masterConnectionString, database);
        try
        {
            await ExecuteAsync(
                databaseConnectionString,
                ReadRepositoryFile(
                    "scripts/data-integration/target-receipt-v1/sqlserver.sql"));
            await ExecuteAsync(
                databaseConnectionString,
                """
                CREATE TABLE dbo.target_rows (id int NOT NULL);
                INSERT INTO dbo.target_rows (id) VALUES (7);
                """);

            var store = new EtlTargetReceiptStore(
                new LocalDbConnectionFactory(databaseConnectionString));
            var evidence = new EtlTargetReceiptEvidence(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Hash('A'),
                Hash('B'),
                Hash('C'),
                Hash('D'),
                Hash('E'),
                Hash('F'),
                EtlWriteMode.Append,
                1);

            async Task AssertInvalidAsync(
                DbConnection connection,
                DbTransaction transaction,
                string provider,
                string expectedDatabase)
            {
                var exception = await Should.ThrowAsync<BusinessException>(() =>
                    store.InsertOrReadAsync(
                        connection,
                        transaction,
                        new EtlTargetReceiptProviderContext(
                            provider,
                            expectedDatabase,
                            Hash('A')),
                        evidence,
                        commandTimeoutSeconds: 5,
                        default));
                exception.Code.ShouldBe(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionInputInvalid);
            }

            await using (var nonSqlTransaction =
                         new RecordingDbTransaction())
            await using (var nonSqlConnection =
                         RecordingDbConnection.ForTarget(
                             nonSqlTransaction,
                             (_, _) => throw new XunitException(
                                 "非 SqlConnection 输入不得创建数据库命令。")))
            {
                await AssertInvalidAsync(
                    nonSqlConnection,
                    nonSqlTransaction,
                    "sqlserver",
                    nonSqlConnection.Database);
                nonSqlConnection.CreatedCommands.ShouldBeEmpty();
            }

            await using var firstConnection =
                new SqlConnection(databaseConnectionString);
            await using var secondConnection =
                new SqlConnection(databaseConnectionString);
            await firstConnection.OpenAsync();
            await secondConnection.OpenAsync();

            await using (var nonSqlTransaction =
                         new RecordingDbTransaction())
            {
                nonSqlTransaction.Attach(firstConnection);
                await AssertInvalidAsync(
                    firstConnection,
                    nonSqlTransaction,
                    "sqlserver",
                    database);
            }

            await using var firstTransaction =
                await firstConnection.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted);
            await using var secondTransaction =
                await secondConnection.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted);
            await AssertInvalidAsync(
                firstConnection,
                secondTransaction,
                "sqlserver",
                database);
            await AssertInvalidAsync(
                firstConnection,
                firstTransaction,
                "mysql",
                database);

            await firstTransaction.RollbackAsync();
            await secondTransaction.RollbackAsync();
            (await QueryLongAsync(
                    databaseConnectionString,
                    "SELECT COUNT_BIG(*) FROM swapmid_etl.ExecutionReceiptV1"))
                .ShouldBe(0);
            (await QueryLongAsync(
                    databaseConnectionString,
                    "SELECT COUNT_BIG(*) FROM dbo.target_rows WHERE id = 7"))
                .ShouldBe(1);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(masterConnectionString, database);
        }
    }

    [SqlServerLocalDbContractFact]
    [Trait("Category", "SqlServerContract")]
    public async Task ReadFence必须等待旧Writer提交并持锁到Handle释放()
    {
        var database = $"SwapMidEtlReceiptFence_{Guid.NewGuid():N}";
        var runtimeLogin = $"swapmid_etl_runtime_{Guid.NewGuid():N}";
        var masterConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 5,
        }.ConnectionString;
        var databaseConnectionString = new SqlConnectionStringBuilder(
            masterConnectionString)
        {
            InitialCatalog = database,
        }.ConnectionString;

        await CreateDatabaseAsync(masterConnectionString, database);
        try
        {
            await ExecuteAsync(
                databaseConnectionString,
                ReadRepositoryFile(
                    "scripts/data-integration/target-receipt-v1/sqlserver.sql"));
            await ExecuteAsync(
                databaseConnectionString,
                "CREATE TABLE dbo.target_rows (id int NOT NULL);");
            var runtimeConnectionString =
                await ConfigureRuntimePrincipalAsync(
                    masterConnectionString,
                    databaseConnectionString,
                    runtimeLogin);

            var target = CreateDataSource(database);
            var store = new EtlTargetReceiptStore(
                new LocalDbConnectionFactory(runtimeConnectionString));
            var transform = EtlTransformConfig.Parse(
                JsonSerializer.Serialize(new
                {
                    sourceQuery = "SELECT 1 AS id",
                    targetTable = "dbo.target_rows",
                    mode = "append-receipt-v1",
                }));
            var activation = await store.ProbeActivationAsync(
                target,
                transform,
                default);
            var evidence = CreateEvidence(activation);
            var context = CreateExecutionContext(
                activation,
                evidence);

            using var deadline = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));
            await using var writerConnection =
                await OpenRuntimeConnectionAsync(
                    runtimeConnectionString,
                    deadline.Token);
            await using var writerTransaction =
                await writerConnection.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    deadline.Token);
            await AcquireWriterLeaseAsync(
                writerConnection,
                writerTransaction,
                activation.CanonicalTargetTable,
                commandTimeoutSeconds: 60,
                deadline.Token);
            var writerSessionId = checked((int)await QueryLongAsync(
                writerConnection,
                writerTransaction,
                "SELECT @@SPID;",
                deadline.Token));
            var lockResource = await ReadGrantedApplicationLockAsync(
                databaseConnectionString,
                writerSessionId,
                deadline.Token);

            (await store.InsertOrReadAsync(
                    writerConnection,
                    writerTransaction,
                    new EtlTargetReceiptProviderContext(
                        activation.Provider,
                        database,
                        activation.TargetCoordinationIdentityHash),
                    evidence,
                    commandTimeoutSeconds: 60,
                    deadline.Token))
                .ShouldBe(EtlTargetReceiptInsertResult.Inserted);

            var readFenceTask = store.AcquireReadFenceAsync(
                target,
                context,
                deadline.Token);
            await WaitForApplicationLockWaiterAsync(
                databaseConnectionString,
                lockResource,
                deadline.Token);
            readFenceTask.IsCompleted.ShouldBeFalse(
                "旧 writer 未提交时 read fence 必须阻塞在同一 applock，" +
                "不得越过锁提前返回 absent");

            await writerTransaction.CommitAsync(deadline.Token);
            await using var readFence = await readFenceTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                deadline.Token);
            readFence.Receipt.ShouldNotBeNull();
            readFence.Receipt.ShouldBe(
                evidence,
                "writer COMMIT 后 read fence 必须在锁内读到 exact receipt");

            await using var contenderConnection =
                new SqlConnection(databaseConnectionString);
            await contenderConnection.OpenAsync(deadline.Token);
            await using var contenderTransaction =
                await contenderConnection.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    deadline.Token);
            var contenderLeaseTask =
                AcquireWriterLeaseAsync(
                    contenderConnection,
                    contenderTransaction,
                    activation.CanonicalTargetTable,
                    commandTimeoutSeconds: 60,
                    deadline.Token);
            await WaitForApplicationLockWaiterAsync(
                databaseConnectionString,
                lockResource,
                deadline.Token);
            contenderLeaseTask.IsCompleted.ShouldBeFalse(
                "read fence handle 未释放时第二 writer 不得取得目标锁");

            await readFence.DisposeAsync();
            await contenderLeaseTask.WaitAsync(
                TimeSpan.FromSeconds(10),
                deadline.Token);
            await contenderTransaction.RollbackAsync(deadline.Token);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            try
            {
                await DropDatabaseAsync(masterConnectionString, database);
            }
            finally
            {
                await DropLoginAsync(masterConnectionString, runtimeLogin);
            }
        }
    }

    private static async Task CreateDatabaseAsync(
        string masterConnectionString,
        string database)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(
            connection,
            $"CREATE DATABASE [{database}];");
    }

    private static async Task DropDatabaseAsync(
        string masterConnectionString,
        string database)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(
            connection,
            $"""
            IF DB_ID(N'{database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{database}]
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{database}];
            END
            """);
    }

    private static async Task DropLoginAsync(
        string masterConnectionString,
        string login)
    {
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(
            connection,
            $"""
            IF SUSER_ID(N'{login}') IS NOT NULL
                DROP LOGIN [{login}];
            """);
    }

    private static async Task ExecuteAsync(
        string connectionString,
        string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, sql);
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ConfigureRuntimePrincipalAsync(
        string masterConnectionString,
        string databaseConnectionString,
        string runtimeLogin,
        bool grantProhibitedControl = false,
        bool grantColumnUpdate = false)
    {
        await ExecuteAsync(
            masterConnectionString,
            $"""
            CREATE LOGIN [{runtimeLogin}]
                WITH PASSWORD = N'{RuntimePassword}',
                     CHECK_POLICY = OFF,
                     CHECK_EXPIRATION = OFF;
            """);
        var forbiddenPermissionSql = grantProhibitedControl
            ? $"""
              GRANT CONTROL
                  ON OBJECT::[swapmid_etl].[TargetResourceV1]
                  TO [{runtimeLogin}];
              """
            : $"""
              DENY INSERT, UPDATE, DELETE, ALTER, TAKE OWNERSHIP
                  ON OBJECT::[swapmid_etl].[TargetResourceV1]
                  TO [{runtimeLogin}];
              DENY UPDATE, DELETE
                  ON OBJECT::[swapmid_etl].[ExecutionReceiptV1]
                  TO [{runtimeLogin}];
              DENY ALTER, TAKE OWNERSHIP
                  ON OBJECT::[swapmid_etl].[ExecutionReceiptV1]
                  TO [{runtimeLogin}];
              """;
        await ExecuteAsync(
            databaseConnectionString,
            $"""
            CREATE USER [{runtimeLogin}] FOR LOGIN [{runtimeLogin}];
            GRANT SELECT
                ON OBJECT::[swapmid_etl].[TargetResourceV1]
                TO [{runtimeLogin}];
            GRANT SELECT, INSERT
                ON OBJECT::[swapmid_etl].[ExecutionReceiptV1]
                TO [{runtimeLogin}];
            GRANT VIEW DEFINITION
                ON OBJECT::[swapmid_etl].[TargetResourceV1]
                TO [{runtimeLogin}];
            GRANT VIEW DEFINITION
                ON OBJECT::[swapmid_etl].[ExecutionReceiptV1]
                TO [{runtimeLogin}];
            {forbiddenPermissionSql}
            {(grantColumnUpdate ? $"GRANT UPDATE ([ResourceInstanceId]) ON OBJECT::[swapmid_etl].[TargetResourceV1] TO [{runtimeLogin}];" : string.Empty)}
            GRANT SELECT, INSERT, DELETE, VIEW DEFINITION
                ON OBJECT::[dbo].[target_rows]
                TO [{runtimeLogin}];
            """);

        return new SqlConnectionStringBuilder(databaseConnectionString)
        {
            IntegratedSecurity = false,
            UserID = runtimeLogin,
            Password = RuntimePassword,
        }.ConnectionString;
    }

    private static async Task<SqlConnection> OpenRuntimeConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<long> QueryLongAsync(
        string connectionString,
        string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> QueryLongAsync(
        SqlConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandText = sql;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<string> ReadGrantedApplicationLockAsync(
        string connectionString,
        int sessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) resource_description
            FROM sys.dm_tran_locks
            WHERE request_session_id = @sessionId
              AND resource_type = N'APPLICATION'
              AND request_status = N'GRANT';
            """;
        command.Parameters.AddWithValue("sessionId", sessionId);
        var result = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken));
        return !string.IsNullOrWhiteSpace(result)
            ? result
            : throw new XunitException(
                "未观察到 writer 已授予的 transaction-owned applock。");
    }

    private static async Task AcquireWriterLeaseAsync(
        SqlConnection connection,
        DbTransaction transaction,
        string targetTable,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var canonicalCommand = connection.CreateCommand();
        canonicalCommand.Transaction = (SqlTransaction)transaction;
        canonicalCommand.CommandTimeout = commandTimeoutSeconds;
        canonicalCommand.CommandText = """
            DECLARE @objectId int = OBJECT_ID(@targetTable, N'U');
            SELECT CASE
                WHEN @objectId IS NULL THEN NULL
                ELSE QUOTENAME(OBJECT_SCHEMA_NAME(@objectId))
                   + N'.'
                   + QUOTENAME(OBJECT_NAME(@objectId))
                END;
            """;
        canonicalCommand.Parameters.AddWithValue(
            "targetTable",
            targetTable);
        var canonicalTarget = Convert.ToString(
            await canonicalCommand.ExecuteScalarAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(canonicalTarget))
        {
            throw new XunitException(
                "writer 测试未能解析规范目标表。");
        }

        var resource =
            "SwapMid:ETL:" +
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    canonicalTarget.ToUpperInvariant())));
        await using var acquireCommand = connection.CreateCommand();
        acquireCommand.Transaction = (SqlTransaction)transaction;
        acquireCommand.CommandTimeout = commandTimeoutSeconds;
        acquireCommand.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = @lockTimeoutMilliseconds;
            SELECT @result;
            """;
        acquireCommand.Parameters.AddWithValue("resource", resource);
        acquireCommand.Parameters.AddWithValue(
            "lockTimeoutMilliseconds",
            checked(commandTimeoutSeconds * 1_000));
        var result = Convert.ToInt32(
            await acquireCommand.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new TimeoutException(
                "LocalDB writer lease could not be acquired.");
        }
    }

    private static async Task WaitForApplicationLockWaiterAsync(
        string connectionString,
        string resourceDescription,
        CancellationToken cancellationToken)
    {
        await using var observer = new SqlConnection(connectionString);
        await observer.OpenAsync(cancellationToken);
        await using var command = observer.CreateCommand();
        command.CommandText = """
            SELECT COUNT_BIG(*)
            FROM sys.dm_tran_locks
            WHERE resource_database_id = DB_ID()
              AND resource_type = N'APPLICATION'
              AND request_status = N'WAIT'
              AND resource_description = @resourceDescription;
            """;
        command.Parameters.AddWithValue(
            "resourceDescription",
            resourceDescription);

        var expiresAt = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < expiresAt)
        {
            if (Convert.ToInt64(
                    await command.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                return;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(25),
                cancellationToken);
        }

        throw new XunitException(
            "10 秒内未观察到相同 applock resource 的等待者。");
    }

    private static EtlTargetReceiptEvidence CreateEvidence(
        EtlTargetReceiptActivation activation) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            activation.ResourceInstanceId,
            Hash('A'),
            activation.VerifiedTargetResourceHash,
            Hash('C'),
            Hash('D'),
            Hash('E'),
            Hash('F'),
            EtlWriteMode.Append,
            1);

    private static EtlExecutionContext CreateExecutionContext(
        EtlTargetReceiptActivation activation,
        EtlTargetReceiptEvidence evidence) =>
        new(
            evidence.RunId,
            evidence.PipelineId,
            evidence.LegacyTargetResourceHash,
            evidence.VerifiedTargetResourceHash,
            evidence.ExecutionContractHash,
            activation.TargetCoordinationIdentityHash,
            evidence.SourceQueryHash,
            activation.CanonicalTargetTable,
            evidence.WriteMode,
            new EtlTargetReceiptContext(evidence.ResourceInstanceId));

    private static string Hash(char value) =>
        new(value, PipelineExecutionConsts.Sha256HexLength);

    private static DataSource CreateDataSource(string database) =>
        new(
            Guid.NewGuid(),
            "target",
            DataSourceType.RelationalDatabase,
            JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                database,
                encrypt = true,
                trustServerCertificate = false,
            }));

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(
                   directory.FullName,
                   "SwapMid.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException(
                "未能定位 SwapMid.slnx");
        }

        return File.ReadAllText(Path.Combine(
            directory.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private sealed class LocalDbConnectionFactory(string connectionString)
        : IEtlRelationalConnectionFactory
    {
        public DbConnection Create(
            string configJson,
            int connectTimeoutSeconds) =>
            new SqlConnection(connectionString);
    }
}
