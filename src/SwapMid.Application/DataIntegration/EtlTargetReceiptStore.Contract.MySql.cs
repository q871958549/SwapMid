using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace SwapMid.DataIntegration;

public sealed partial class EtlTargetReceiptStore
{
    private static readonly MySqlColumnShape[] MySqlContractColumns =
    [
        new("swapmid_etl_target_resource_v1", 1, "singleton_id", "tinyint unsigned", 3, 0, null, null),
        new("swapmid_etl_target_resource_v1", 2, "protocol_version", "smallint unsigned", 5, 0, null, null),
        new("swapmid_etl_target_resource_v1", 3, "resource_instance_id", "binary(16)", null, null, 16, null),
        new("swapmid_etl_target_resource_v1", 4, "created_at_utc", "datetime(6)", null, null, null, 6, IsUtcDefault: true),
        new("swapmid_etl_execution_receipt_v1", 1, "run_id", "binary(16)", null, null, 16, null),
        new("swapmid_etl_execution_receipt_v1", 2, "protocol_version", "smallint unsigned", 5, 0, null, null),
        new("swapmid_etl_execution_receipt_v1", 3, "resource_instance_id", "binary(16)", null, null, 16, null),
        new("swapmid_etl_execution_receipt_v1", 4, "pipeline_id", "binary(16)", null, null, 16, null),
        new("swapmid_etl_execution_receipt_v1", 5, "legacy_target_resource_hash", "binary(32)", null, null, 32, null),
        new("swapmid_etl_execution_receipt_v1", 6, "verified_target_resource_hash", "binary(32)", null, null, 32, null),
        new("swapmid_etl_execution_receipt_v1", 7, "execution_contract_hash", "binary(32)", null, null, 32, null),
        new("swapmid_etl_execution_receipt_v1", 8, "source_query_hash", "binary(32)", null, null, 32, null),
        new("swapmid_etl_execution_receipt_v1", 9, "target_schema_hash", "binary(32)", null, null, 32, null),
        new("swapmid_etl_execution_receipt_v1", 10, "batch_digest", "binary(32)", null, null, 32, null),
        new("swapmid_etl_execution_receipt_v1", 11, "write_mode", "tinyint unsigned", 3, 0, null, null),
        new("swapmid_etl_execution_receipt_v1", 12, "processed_rows", "bigint", 19, 0, null, null),
        new("swapmid_etl_execution_receipt_v1", 13, "recorded_at_utc", "datetime(6)", null, null, null, 6, IsUtcDefault: true),
    ];

    private static async Task EnsureMySqlContractShapeAsync(
        DbConnection connection,
        DbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await EnsureMySqlTriggerCatalogVisibilityAsync(
            connection,
            transaction,
            commandTimeoutSeconds,
            cancellationToken);

        const string capabilitySql = """
            SELECT IF(
                (
                    SELECT COUNT(*)
                    FROM information_schema.TABLES
                    WHERE BINARY TABLE_SCHEMA = BINARY DATABASE()
                      AND
                      (
                          BINARY TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                          OR BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
                      )
                      AND TABLE_TYPE = 'BASE TABLE'
                      AND ENGINE = 'InnoDB'
                ) = 2
                AND NOT EXISTS
                (
                    SELECT 1 FROM information_schema.TRIGGERS
                    WHERE BINARY TRIGGER_SCHEMA = BINARY DATABASE()
                      AND
                      (
                          BINARY EVENT_OBJECT_TABLE = BINARY 'swapmid_etl_target_resource_v1'
                          OR BINARY EVENT_OBJECT_TABLE = BINARY 'swapmid_etl_execution_receipt_v1'
                      )
                )
                AND NOT EXISTS
                (
                    SELECT 1 FROM information_schema.PARTITIONS
                    WHERE BINARY TABLE_SCHEMA = BINARY DATABASE()
                      AND
                      (
                          BINARY TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                          OR BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
                      )
                      AND PARTITION_NAME IS NOT NULL
                )
                AND
                (
                    SELECT COUNT(*)
                    FROM information_schema.TABLE_CONSTRAINTS
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND
                      (
                          BINARY TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                          OR BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
                      )
                ) = 9
                AND
                (
                    SELECT COUNT(*)
                    FROM information_schema.TABLE_CONSTRAINTS
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND CONSTRAINT_TYPE = 'CHECK'
                      AND CONSTRAINT_NAME IN
                      (
                          'CK_swapmid_etl_target_resource_v1_singleton_id',
                          'CK_swapmid_etl_target_resource_v1_protocol_version',
                          'CK_swapmid_etl_execution_receipt_v1_protocol_version',
                          'CK_swapmid_etl_execution_receipt_v1_write_mode',
                          'CK_swapmid_etl_execution_receipt_v1_processed_rows'
                      )
                ) = 5
                AND EXISTS
                (
                    SELECT 1
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                      AND CONSTRAINT_NAME = 'PRIMARY'
                      AND BINARY COLUMN_NAME = BINARY 'singleton_id'
                      AND ORDINAL_POSITION = 1
                )
                AND
                (
                    SELECT COUNT(*)
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                      AND CONSTRAINT_NAME = 'PRIMARY'
                ) = 1
                AND EXISTS
                (
                    SELECT 1
                    FROM information_schema.TABLE_CONSTRAINTS AS tc
                    INNER JOIN information_schema.KEY_COLUMN_USAGE AS kcu
                        ON BINARY kcu.CONSTRAINT_SCHEMA = BINARY tc.CONSTRAINT_SCHEMA
                       AND BINARY kcu.TABLE_NAME = BINARY tc.TABLE_NAME
                       AND BINARY kcu.CONSTRAINT_NAME = BINARY tc.CONSTRAINT_NAME
                    WHERE BINARY tc.CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY tc.TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                      AND tc.CONSTRAINT_TYPE = 'UNIQUE'
                      AND BINARY tc.CONSTRAINT_NAME =
                          BINARY 'UQ_swapmid_etl_target_resource_v1_resource_instance_id'
                      AND BINARY kcu.COLUMN_NAME = BINARY 'resource_instance_id'
                      AND kcu.ORDINAL_POSITION = 1
                )
                AND
                (
                    SELECT COUNT(*)
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                      AND BINARY CONSTRAINT_NAME =
                          BINARY 'UQ_swapmid_etl_target_resource_v1_resource_instance_id'
                ) = 1
                AND EXISTS
                (
                    SELECT 1
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
                      AND CONSTRAINT_NAME = 'PRIMARY'
                      AND BINARY COLUMN_NAME = BINARY 'run_id'
                      AND ORDINAL_POSITION = 1
                )
                AND
                (
                    SELECT COUNT(*)
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
                      AND CONSTRAINT_NAME = 'PRIMARY'
                ) = 1
                AND
                (
                    SELECT COUNT(*)
                    FROM information_schema.REFERENTIAL_CONSTRAINTS
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
                ) = 1
                AND EXISTS
                (
                    SELECT 1
                    FROM information_schema.REFERENTIAL_CONSTRAINTS AS rc
                    INNER JOIN information_schema.KEY_COLUMN_USAGE AS kcu
                        ON BINARY kcu.CONSTRAINT_SCHEMA = BINARY rc.CONSTRAINT_SCHEMA
                       AND BINARY kcu.TABLE_NAME = BINARY rc.TABLE_NAME
                       AND BINARY kcu.CONSTRAINT_NAME = BINARY rc.CONSTRAINT_NAME
                    WHERE BINARY rc.CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY rc.TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
                      AND BINARY rc.CONSTRAINT_NAME =
                          BINARY 'FK_swapmid_etl_execution_receipt_v1_target_resource_v1'
                      AND BINARY rc.REFERENCED_TABLE_NAME =
                          BINARY 'swapmid_etl_target_resource_v1'
                      AND rc.UPDATE_RULE IN ('RESTRICT', 'NO ACTION')
                      AND rc.DELETE_RULE IN ('RESTRICT', 'NO ACTION')
                      AND BINARY kcu.COLUMN_NAME = BINARY 'resource_instance_id'
                      AND BINARY kcu.REFERENCED_COLUMN_NAME = BINARY 'resource_instance_id'
                )
                AND
                (
                    SELECT COUNT(*)
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE BINARY CONSTRAINT_SCHEMA = BINARY DATABASE()
                      AND BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
                      AND BINARY CONSTRAINT_NAME =
                          BINARY 'FK_swapmid_etl_execution_receipt_v1_target_resource_v1'
                ) = 1,
                1,
                0);
            """;
        var contractSupported = await connection.QuerySingleAsync<long>(
            new CommandDefinition(
                capabilitySql,
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: cancellationToken));
        if (contractSupported != 1)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        const string columnsSql = """
            SELECT
                TABLE_NAME AS TableName,
                ORDINAL_POSITION AS OrdinalPosition,
                COLUMN_NAME AS ColumnName,
                LOWER(COLUMN_TYPE) AS ColumnType,
                NUMERIC_PRECISION AS NumericPrecision,
                NUMERIC_SCALE AS NumericScale,
                CHARACTER_MAXIMUM_LENGTH AS CharacterMaximumLength,
                DATETIME_PRECISION AS DateTimePrecision,
                IS_NULLABLE AS IsNullable,
                COLUMN_DEFAULT AS ColumnDefault,
                LOWER(EXTRA) AS Extra,
                GENERATION_EXPRESSION AS GenerationExpression
            FROM information_schema.COLUMNS
            WHERE BINARY TABLE_SCHEMA = BINARY DATABASE()
              AND
              (
                  BINARY TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                  OR BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
              )
            ORDER BY
                CASE TABLE_NAME
                    WHEN 'swapmid_etl_target_resource_v1' THEN 0
                    ELSE 1
                END,
                ORDINAL_POSITION;
            """;
        var columns = (await connection.QueryAsync<MySqlColumnRow>(
            new CommandDefinition(
                columnsSql,
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
        if (columns.Length != MySqlContractColumns.Length)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        for (var index = 0; index < columns.Length; index++)
        {
            var actual = columns[index];
            var expected = MySqlContractColumns[index];
            var normalizedDefault = NormalizeMySqlDefault(actual.ColumnDefault);
            var extra = actual.Extra?.Trim() ?? string.Empty;
            if (!string.Equals(actual.TableName, expected.TableName, StringComparison.Ordinal) ||
                actual.OrdinalPosition != expected.OrdinalPosition ||
                !string.Equals(actual.ColumnName, expected.ColumnName, StringComparison.Ordinal) ||
                !string.Equals(actual.ColumnType, expected.ColumnType, StringComparison.Ordinal) ||
                actual.NumericPrecision != expected.NumericPrecision ||
                actual.NumericScale != expected.NumericScale ||
                actual.CharacterMaximumLength != expected.CharacterMaximumLength ||
                actual.DateTimePrecision != expected.DateTimePrecision ||
                !string.Equals(actual.IsNullable, "NO", StringComparison.Ordinal) ||
                (!expected.IsUtcDefault && normalizedDefault != null) ||
                (expected.IsUtcDefault && normalizedDefault != "utc_timestamp(6)") ||
                (!string.IsNullOrEmpty(extra) &&
                 !(expected.IsUtcDefault && extra == "default_generated")) ||
                !string.IsNullOrEmpty(actual.GenerationExpression))
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }
    }

    private static async Task EnsureMySqlTriggerCatalogVisibilityAsync(
        DbConnection connection,
        DbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var currentUser = await connection.QuerySingleAsync<string>(
            new CommandDefinition(
                "SELECT CURRENT_USER()",
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: cancellationToken));
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
            SELECT COUNT(DISTINCT TABLE_NAME)
            FROM information_schema.TABLE_PRIVILEGES
            WHERE GRANTEE = @grantee
              AND BINARY TABLE_SCHEMA = BINARY DATABASE()
              AND
              (
                  BINARY TABLE_NAME = BINARY 'swapmid_etl_target_resource_v1'
                  OR BINARY TABLE_NAME = BINARY 'swapmid_etl_execution_receipt_v1'
              )
              AND PRIVILEGE_TYPE = 'TRIGGER';
            """;
        var visibleTableCount = await connection.QuerySingleAsync<long>(
            new CommandDefinition(
                privilegeSql,
                new { grantee },
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: cancellationToken));
        if (visibleTableCount != 2)
        {
            // INFORMATION_SCHEMA.TRIGGERS 对无权限对象会返回假空集。只接受当前账号
            // 对两个固定表的直接精确授权，拒绝角色/全局授权与 partial revoke 歧义。
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    private static string? NormalizeMySqlDefault(string? value)
    {
        if (value == null)
        {
            return null;
        }

        var normalized = string.Concat(
            value.Where(character => !char.IsWhiteSpace(character)))
            .ToLowerInvariant();
        while (normalized.Length >= 2 &&
               normalized[0] == '(' &&
               normalized[^1] == ')')
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }

    private sealed record MySqlColumnShape(
        string TableName,
        int OrdinalPosition,
        string ColumnName,
        string ColumnType,
        long? NumericPrecision,
        long? NumericScale,
        long? CharacterMaximumLength,
        long? DateTimePrecision,
        bool IsUtcDefault = false);

    private sealed class MySqlColumnRow
    {
        public string TableName { get; set; } = string.Empty;
        public int OrdinalPosition { get; set; }
        public string ColumnName { get; set; } = string.Empty;
        public string ColumnType { get; set; } = string.Empty;
        public long? NumericPrecision { get; set; }
        public long? NumericScale { get; set; }
        public long? CharacterMaximumLength { get; set; }
        public long? DateTimePrecision { get; set; }
        public string IsNullable { get; set; } = string.Empty;
        public string? ColumnDefault { get; set; }
        public string? Extra { get; set; }
        public string? GenerationExpression { get; set; }
    }
}
