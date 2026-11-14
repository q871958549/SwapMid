using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

/// <summary>
/// 目标库 provider 前置能力验证。所有检查均在目标事务与 DELETE 之前完成，
/// 防止“代码声称原子、实际引擎/trigger/packet 语义不支持”。
/// </summary>
internal static class EtlTargetPreflight
{
    internal sealed record Preparation(
        long BatchPayloadLimit,
        string SchemaFingerprint,
        string CanonicalTargetTable);

    internal static async Task<Preparation> PrepareAsync(
        DbConnection targetConnection,
        string targetTable,
        IReadOnlyList<string> sourceColumns,
        RelationalDataSourceConnection.Probe targetProbe,
        bool truncate,
        long maxBatchPayloadBytes,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var canonicalTargetTable =
            await EtlTargetSession.ResolveCanonicalTargetTableAsync(
                targetConnection,
                targetTable,
                targetProbe,
                commandTimeoutSeconds,
                ct);
        if (targetProbe.NormalizedProvider is "sqlserver" or "mssql")
        {
            if (targetConnection is SqlConnection)
            {
                await EtlTargetSession.ConfigureForStagingAsync(
                    targetConnection,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct);
                await EnsureSqlServerTargetAsync(
                    targetConnection,
                    targetTransaction: null,
                    canonicalTargetTable,
                    truncate,
                    commandTimeoutSeconds,
                    ct);
                await EtlTargetColumnContract.ValidateAsync(
                    targetConnection,
                    canonicalTargetTable,
                    sourceColumns,
                    truncate,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct);
            }

            return new Preparation(
                maxBatchPayloadBytes,
                await EtlTargetSchemaFingerprint.ReadAsync(
                    targetConnection,
                    transaction: null,
                    canonicalTargetTable,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct),
                canonicalTargetTable);
        }

        if (targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql")
        {
            if (targetConnection is NpgsqlConnection)
            {
                await EnsurePostgreSqlTargetAsync(
                    targetConnection,
                    targetTransaction: null,
                    canonicalTargetTable,
                    truncate,
                    commandTimeoutSeconds,
                    ct);
                await EtlTargetColumnContract.ValidateAsync(
                    targetConnection,
                    canonicalTargetTable,
                    sourceColumns,
                    truncate,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct);
            }

            return new Preparation(
                maxBatchPayloadBytes,
                await EtlTargetSchemaFingerprint.ReadAsync(
                    targetConnection,
                    transaction: null,
                    canonicalTargetTable,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct),
                canonicalTargetTable);
        }

        if (targetProbe.NormalizedProvider is not ("mysql" or "mariadb"))
        {
            return new Preparation(
                maxBatchPayloadBytes,
                await EtlTargetSchemaFingerprint.ReadAsync(
                    targetConnection,
                    transaction: null,
                    canonicalTargetTable,
                    targetProbe,
                    commandTimeoutSeconds,
                    ct),
                canonicalTargetTable);
        }

        if (truncate)
        {
            // MySQL 没有只读 metadata 权限可完整证明跨 schema 入站级联 FK；
            // INFORMATION_SCHEMA 对无权限对象返回假空集。专用目标 schema /
            // 独立 metadata 审计凭据落地前，不能声称 truncate 全量替换无旁路副作用。
            throw EtlPipelineRunner.InvalidInput();
        }

        await EnsureMySqlTargetEngineAsync(
            targetConnection,
            targetTransaction: null,
            canonicalTargetTable,
            truncate,
            commandTimeoutSeconds,
            ct);
        await EtlTargetColumnContract.ValidateAsync(
            targetConnection,
            canonicalTargetTable,
            sourceColumns,
            truncate,
            targetProbe,
            commandTimeoutSeconds,
            ct);

        await targetConnection.ExecuteAsync(new CommandDefinition(
            """
            SET SESSION sql_mode = CONCAT_WS(
                ',',
                NULLIF(@@SESSION.sql_mode, ''),
                IF(
                    FIND_IN_SET('STRICT_ALL_TABLES', @@SESSION.sql_mode) = 0,
                    'STRICT_ALL_TABLES',
                    NULL))
            """,
            commandTimeout: commandTimeoutSeconds,
            cancellationToken: ct));

        var maxAllowedPacket = await targetConnection.QuerySingleAsync<long>(
            new CommandDefinition(
                "SELECT @@SESSION.max_allowed_packet",
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        var packetSafePayload = maxAllowedPacket / 4;
        if (packetSafePayload <= 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        // MySqlConnector 未 prepare 时会把参数编码进 SQL；二进制/转义最坏可接近 2x，
        // utf8mb4 也可能高于托管 UTF-16 估算。只用 packet 四分之一，剩余空间覆盖
        // 值序列化膨胀、SQL/列名/占位符文本、参数元数据与协议帧。
        return new Preparation(
            Math.Min(maxBatchPayloadBytes, packetSafePayload),
            await EtlTargetSchemaFingerprint.ReadAsync(
                targetConnection,
                transaction: null,
                canonicalTargetTable,
                targetProbe,
                commandTimeoutSeconds,
                ct),
            canonicalTargetTable);
    }

    internal static async Task<string> RevalidateLockedAsync(
        DbConnection targetConnection,
        DbTransaction targetTransaction,
        string targetTable,
        RelationalDataSourceConnection.Probe targetProbe,
        bool truncate,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (targetConnection is SqlConnection &&
            targetProbe.NormalizedProvider is "sqlserver" or "mssql")
        {
            await EnsureSqlServerTargetAsync(
                targetConnection,
                targetTransaction,
                targetTable,
                truncate,
                commandTimeoutSeconds,
                ct);
        }
        else if (targetConnection is MySqlConnection &&
                 targetProbe.NormalizedProvider is "mysql" or "mariadb")
        {
            await EnsureMySqlTargetEngineAsync(
                targetConnection,
                targetTransaction,
                targetTable,
                truncate,
                commandTimeoutSeconds,
                ct);
        }
        else if (targetConnection is NpgsqlConnection &&
                 targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql")
        {
            await EnsurePostgreSqlTargetAsync(
                targetConnection,
                targetTransaction,
                targetTable,
                truncate,
                commandTimeoutSeconds,
                ct);
        }

        return await EtlTargetSchemaFingerprint.ReadAsync(
            targetConnection,
            targetTransaction,
            targetTable,
            targetProbe,
            commandTimeoutSeconds,
            ct);
    }

    private static async Task EnsureSqlServerTargetAsync(
        DbConnection targetConnection,
        DbTransaction? targetTransaction,
        string targetTable,
        bool truncate,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var parts = targetTable.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);
        var schema = parts.Length == 2 ? parts[0] : null;
        var table = parts[^1];
        var capabilities =
            await targetConnection
                .QuerySingleAsync<SqlServerCapabilities>(
                    new CommandDefinition(
                        """
                        SELECT
                            CONVERT(
                                int,
                                SERVERPROPERTY(
                                    'EngineEdition')) AS EngineEdition,
                            CONVERT(
                                int,
                                PARSENAME(
                                    CONVERT(
                                        varchar(32),
                                        SERVERPROPERTY(
                                            'ProductVersion')),
                                    4)) AS MajorVersion
                        """,
                        transaction: targetTransaction,
                        commandTimeout: commandTimeoutSeconds,
                        cancellationToken: ct));
        var isAzureSql =
            capabilities.EngineEdition is 5 or 8;
        if (!isAzureSql &&
            (capabilities.EngineEdition is not (2 or 3 or 4) ||
             capabilities.MajorVersion < 13))
        {
            // 正式支持边界为 SQL Server 2016+、Azure SQL Database/MI；
            // Synapse/Fabric/Edge 与旧盒装实例的锁、事务和 catalog 语义不在契约内。
            throw EtlPipelineRunner.InvalidInput();
        }

        const string rlsGuardSql = """
            AND NOT EXISTS
            (
                SELECT 1
                FROM sys.security_predicates AS sp
                INNER JOIN sys.security_policies AS pol
                    ON pol.object_id = sp.object_id
                INNER JOIN sys.tables AS t
                    ON t.object_id = sp.target_object_id
                INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                WHERE s.name = COALESCE(@schema, SCHEMA_NAME())
                  AND t.name = @table
                  AND pol.is_enabled = 1
            )
            """;
        var graphGuardSql =
            isAzureSql || capabilities.MajorVersion >= 14
                ? "AND t.is_node = 0 AND t.is_edge = 0"
                : string.Empty;
        var ledgerGuardSql =
            isAzureSql || capabilities.MajorVersion >= 16
                ? "AND t.ledger_type = 0"
                : string.Empty;
        var targetSql = $"""
            SELECT CONVERT(bit, CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM sys.tables AS t
                    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                    WHERE s.name = COALESCE(@schema, SCHEMA_NAME())
                      AND t.name = @table
                      AND t.temporal_type = 0
                      AND t.is_filetable = 0
                      AND t.is_memory_optimized = 0
                      AND t.is_remote_data_archive_enabled = 0
                      AND t.is_external = 0
                      {graphGuardSql}
                      {ledgerGuardSql}
                      AND HAS_PERMS_BY_NAME(
                              DB_NAME(),
                              N'DATABASE',
                              N'VIEW DEFINITION') = 1
                )
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM sys.triggers AS tr
                    INNER JOIN sys.trigger_events AS ev
                        ON ev.object_id = tr.object_id
                    INNER JOIN sys.tables AS t ON t.object_id = tr.parent_id
                    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                    WHERE s.name = COALESCE(@schema, SCHEMA_NAME())
                      AND t.name = @table
                      AND tr.is_disabled = 0
                      AND
                      (
                          ev.type_desc = N'INSERT'
                          OR (@truncate = 1 AND ev.type_desc = N'DELETE')
                      )
                )
                AND
                (
                    @truncate = 0
                    OR NOT EXISTS
                    (
                        SELECT 1
                        FROM sys.foreign_keys AS fk
                        INNER JOIN sys.tables AS t
                            ON t.object_id = fk.referenced_object_id
                        INNER JOIN sys.schemas AS s
                            ON s.schema_id = t.schema_id
                        WHERE s.name = COALESCE(@schema, SCHEMA_NAME())
                          AND t.name = @table
                          AND fk.is_disabled = 0
                          AND fk.delete_referential_action <> 0
                    )
                )
                {rlsGuardSql}
                THEN 1 ELSE 0 END)
            """;
        var isSupported = await targetConnection.QuerySingleAsync<bool>(
            new CommandDefinition(
                targetSql,
                new { schema, table, truncate },
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (!isSupported)
        {
            // OUTPUT 在 trigger 执行前返回；INSERT trigger 可吞行/删改 inserted，
            // DELETE trigger 可让 truncate 留旧行。RLS 又会让 DELETE 只处理当前身份
            // 可见行，且同一身份的事后 COUNT 仍看不到隐藏行。没有业务主键/特权对账
            // 无法证明结果，因此这些目标形态全部 fail-closed。
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    private static async Task EnsureMySqlTargetEngineAsync(
        DbConnection targetConnection,
        DbTransaction? targetTransaction,
        string targetTable,
        bool truncate,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var parts = targetTable.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);
        var schema = parts.Length == 2
            ? parts[0]
            : targetConnection.Database;
        var table = parts[^1];
        const string engineSql = """
            SELECT CASE
                WHEN t.ENGINE = 'InnoDB'
                 AND COALESCE(
                         UPPER(t.ROW_FORMAT),
                         '') <> 'COMPRESSED'
                 AND NOT EXISTS
                 (
                     SELECT 1
                     FROM information_schema.PARTITIONS AS p
                     WHERE BINARY p.TABLE_SCHEMA =
                           BINARY t.TABLE_SCHEMA
                       AND BINARY p.TABLE_NAME =
                           BINARY t.TABLE_NAME
                       AND p.PARTITION_NAME IS NOT NULL
                 )
                 AND NOT EXISTS
                 (
                     SELECT 1
                     FROM information_schema.STATISTICS AS ft
                     WHERE BINARY ft.TABLE_SCHEMA =
                           BINARY t.TABLE_SCHEMA
                       AND BINARY ft.TABLE_NAME =
                           BINARY t.TABLE_NAME
                       AND UPPER(ft.INDEX_TYPE) = 'FULLTEXT'
                 )
                 AND
                 (
                     SELECT COUNT(*)
                     FROM information_schema.COLUMNS AS c
                     WHERE BINARY c.TABLE_SCHEMA =
                           BINARY t.TABLE_SCHEMA
                       AND BINARY c.TABLE_NAME =
                           BINARY t.TABLE_NAME
                 ) < 1000
                 AND
                 (
                     SELECT COUNT(DISTINCT s.INDEX_NAME)
                     FROM information_schema.STATISTICS AS s
                     WHERE BINARY s.TABLE_SCHEMA =
                           BINARY t.TABLE_SCHEMA
                       AND BINARY s.TABLE_NAME =
                           BINARY t.TABLE_NAME
                 ) < 63
                THEN 1 ELSE 0 END
            FROM information_schema.TABLES AS t
            WHERE BINARY t.TABLE_SCHEMA = BINARY @schema
              AND BINARY t.TABLE_NAME = BINARY @table
            """;
        var isSupported = await targetConnection.QuerySingleOrDefaultAsync<bool>(
            new CommandDefinition(
                engineSql,
                new { schema, table },
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (!isSupported)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        await EnsureMySqlTargetShapeAsync(
            targetConnection,
            targetTransaction,
            schema,
            table,
            commandTimeoutSeconds,
            ct);
    }

    private static async Task EnsureMySqlTargetShapeAsync(
        DbConnection targetConnection,
        DbTransaction? targetTransaction,
        string schema,
        string table,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var currentUser = await targetConnection.QuerySingleAsync<string>(
            new CommandDefinition(
                "SELECT CURRENT_USER()",
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        var separator = currentUser.LastIndexOf('@');
        if (separator <= 0 ||
            separator == currentUser.Length - 1 ||
            currentUser.Contains('\''))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var grantee =
            $"'{currentUser[..separator]}'@'{currentUser[(separator + 1)..]}'";
        const string privilegeSql = """
            SELECT COUNT(DISTINCT PRIVILEGE_TYPE)
            FROM information_schema.TABLE_PRIVILEGES
            WHERE GRANTEE = @grantee
              AND BINARY TABLE_SCHEMA = BINARY @schema
              AND BINARY TABLE_NAME = BINARY @table
              AND
              PRIVILEGE_TYPE = 'TRIGGER'
            """;
        var privilegeCount = await targetConnection.QuerySingleAsync<int>(
            new CommandDefinition(
                privilegeSql,
                new { grantee, schema, table },
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (privilegeCount != 1)
        {
            // TRIGGERS catalog 无权限时会返回假空集；只接受当前账号对该目标表
            // 的直接精确授权，拒绝角色/全局授权与 partial revoke 的歧义。
            throw EtlPipelineRunner.InvalidInput();
        }

        const string triggerSql = """
            SELECT COUNT(*)
            FROM information_schema.TRIGGERS
            WHERE BINARY EVENT_OBJECT_SCHEMA = BINARY @schema
              AND BINARY EVENT_OBJECT_TABLE = BINARY @table
              AND
              EVENT_MANIPULATION = 'INSERT'
            """;
        var triggerCount = await targetConnection.QuerySingleAsync<int>(
            new CommandDefinition(
                triggerSql,
                new { schema, table },
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (triggerCount != 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

    }

    private static async Task EnsurePostgreSqlTargetAsync(
        DbConnection targetConnection,
        DbTransaction? targetTransaction,
        string targetTable,
        bool truncate,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        var parts = targetTable.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);
        var schema = parts.Length == 2 ? parts[0] : "public";
        var table = parts[^1];

        const string targetSql = """
            SELECT (
                current_setting('server_version_num')::int >= 120000
                AND NOT pg_catalog.pg_is_in_recovery()
                AND EXISTS (
                    SELECT 1
                    FROM pg_catalog.pg_class AS c
                    INNER JOIN pg_catalog.pg_namespace AS n
                        ON n.oid = c.relnamespace
                    WHERE n.nspname = @schema
                      AND c.relname = @table
                      AND c.relkind = 'r'
                      AND c.relpersistence = 'p'
                      AND NOT c.relrowsecurity
                      AND NOT c.relforcerowsecurity
                      AND NOT c.relhassubclass
                      AND NOT EXISTS (
                          SELECT 1
                          FROM pg_catalog.pg_rewrite AS rw
                          WHERE rw.ev_class = c.oid
                            AND rw.rulename <> '_RETURN'
                      )
                      AND NOT EXISTS (
                          SELECT 1
                          FROM pg_catalog.pg_trigger AS tr
                          WHERE tr.tgrelid = c.oid
                            AND NOT tr.tgisinternal
                            AND tr.tgenabled <> 'D'
                            AND (
                                (tr.tgtype & 4) <> 0
                                OR (@truncate AND (tr.tgtype & 8) <> 0)
                            )
                      )
                      AND (
                          NOT @truncate
                          OR NOT EXISTS (
                              SELECT 1
                              FROM pg_catalog.pg_constraint AS fk
                              WHERE fk.confrelid = c.oid
                                AND fk.contype = 'f'
                                AND fk.confdeltype NOT IN ('a', 'r')
                          )
                      )
                )
            )
            """;

        var isSupported = await targetConnection.QuerySingleAsync<bool>(
            new CommandDefinition(
                targetSql,
                new { schema, table, truncate },
                transaction: targetTransaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        if (!isSupported)
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    private sealed class SqlServerCapabilities
    {
        public int EngineEdition { get; set; }
        public int MajorVersion { get; set; }
    }
}
