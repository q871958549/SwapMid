using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace SwapMid.DataIntegration;

public sealed partial class EtlTargetReceiptStore
{
    private static readonly SqlServerColumnShape[] SqlServerContractColumns =
    [
        new("TargetResourceV1", 1, "SingletonId", "tinyint", 1, 3, 0),
        new("TargetResourceV1", 2, "ProtocolVersion", "smallint", 2, 5, 0),
        new("TargetResourceV1", 3, "ResourceInstanceId", "uniqueidentifier", 16, 0, 0),
        new("TargetResourceV1", 4, "CreatedAtUtc", "datetime2", 8, 26, 6),
        new("ExecutionReceiptV1", 1, "RunId", "uniqueidentifier", 16, 0, 0),
        new("ExecutionReceiptV1", 2, "ProtocolVersion", "smallint", 2, 5, 0),
        new("ExecutionReceiptV1", 3, "ResourceInstanceId", "uniqueidentifier", 16, 0, 0),
        new("ExecutionReceiptV1", 4, "PipelineId", "uniqueidentifier", 16, 0, 0),
        new("ExecutionReceiptV1", 5, "LegacyTargetResourceHash", "binary", 32, 0, 0),
        new("ExecutionReceiptV1", 6, "VerifiedTargetResourceHash", "binary", 32, 0, 0),
        new("ExecutionReceiptV1", 7, "ExecutionContractHash", "binary", 32, 0, 0),
        new("ExecutionReceiptV1", 8, "SourceQueryHash", "binary", 32, 0, 0),
        new("ExecutionReceiptV1", 9, "TargetSchemaHash", "binary", 32, 0, 0),
        new("ExecutionReceiptV1", 10, "BatchDigest", "binary", 32, 0, 0),
        new("ExecutionReceiptV1", 11, "WriteMode", "tinyint", 1, 3, 0),
        new("ExecutionReceiptV1", 12, "ProcessedRows", "bigint", 8, 19, 0),
        new("ExecutionReceiptV1", 13, "RecordedAtUtc", "datetime2", 8, 26, 6),
    ];

    private static async Task EnsureSqlServerContractShapeAsync(
        DbConnection connection,
        DbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        const string capabilitySql = """
            DECLARE @control int = OBJECT_ID(N'[swapmid_etl].[TargetResourceV1]', N'U');
            DECLARE @receipt int = OBJECT_ID(N'[swapmid_etl].[ExecutionReceiptV1]', N'U');

            SELECT CONVERT(bit, CASE WHEN
                HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[TargetResourceV1]',
                    N'OBJECT',
                    N'VIEW DEFINITION') = 1
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[ExecutionReceiptV1]',
                    N'OBJECT',
                    N'VIEW DEFINITION') = 1
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[TargetResourceV1]',
                    N'OBJECT',
                    N'INSERT') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[TargetResourceV1]',
                    N'OBJECT',
                    N'UPDATE') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[TargetResourceV1]',
                    N'OBJECT',
                    N'DELETE') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[TargetResourceV1]',
                    N'OBJECT',
                    N'ALTER') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[TargetResourceV1]',
                    N'OBJECT',
                    N'CONTROL') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[TargetResourceV1]',
                    N'OBJECT',
                    N'TAKE OWNERSHIP') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[ExecutionReceiptV1]',
                    N'OBJECT',
                    N'UPDATE') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[ExecutionReceiptV1]',
                    N'OBJECT',
                    N'DELETE') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[ExecutionReceiptV1]',
                    N'OBJECT',
                    N'ALTER') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[ExecutionReceiptV1]',
                    N'OBJECT',
                    N'CONTROL') = 0
                AND HAS_PERMS_BY_NAME(
                    N'[swapmid_etl].[ExecutionReceiptV1]',
                    N'OBJECT',
                    N'TAKE OWNERSHIP') = 0
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM sys.columns AS column_permission
                    WHERE column_permission.object_id IN (@control, @receipt)
                      AND HAS_PERMS_BY_NAME(
                          QUOTENAME(OBJECT_SCHEMA_NAME(column_permission.object_id)) +
                              N'.' + QUOTENAME(OBJECT_NAME(column_permission.object_id)),
                          N'OBJECT',
                          N'UPDATE',
                          column_permission.name,
                          N'COLUMN') = 1
                )
                AND @control IS NOT NULL
                AND @receipt IS NOT NULL
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM sys.tables
                    WHERE object_id IN (@control, @receipt)
                      AND
                      (
                          temporal_type <> 0
                          OR is_memory_optimized <> 0
                          OR is_external <> 0
                          OR is_filetable <> 0
                          OR is_remote_data_archive_enabled <> 0
                      )
                )
                AND NOT EXISTS
                (
                    SELECT 1 FROM sys.triggers
                    WHERE parent_id IN (@control, @receipt)
                )
                AND NOT EXISTS
                (
                    SELECT 1 FROM sys.security_predicates
                    WHERE target_object_id IN (@control, @receipt)
                )
                AND
                (
                    SELECT COUNT(*) FROM sys.key_constraints
                    WHERE parent_object_id IN (@control, @receipt)
                ) = 3
                AND EXISTS
                (
                    SELECT 1 FROM sys.key_constraints
                    WHERE parent_object_id = @control
                      AND name = N'PK_swapmid_etl_TargetResourceV1'
                      AND type = N'PK'
                )
                AND EXISTS
                (
                    SELECT 1 FROM sys.key_constraints
                    WHERE parent_object_id = @control
                      AND name = N'UQ_swapmid_etl_TargetResourceV1_ResourceInstanceId'
                      AND type = N'UQ'
                )
                AND EXISTS
                (
                    SELECT 1 FROM sys.key_constraints
                    WHERE parent_object_id = @receipt
                      AND name = N'PK_swapmid_etl_ExecutionReceiptV1'
                      AND type = N'PK'
                )
                AND
                (
                    SELECT COUNT(*)
                    FROM sys.indexes AS i
                    INNER JOIN sys.index_columns AS ic
                        ON ic.object_id = i.object_id
                       AND ic.index_id = i.index_id
                       AND ic.key_ordinal > 0
                    INNER JOIN sys.columns AS c
                        ON c.object_id = ic.object_id
                       AND c.column_id = ic.column_id
                    WHERE i.object_id = @control
                      AND i.is_primary_key = 1
                      AND c.name = N'SingletonId'
                ) = 1
                AND
                (
                    SELECT COUNT(*)
                    FROM sys.indexes AS i
                    INNER JOIN sys.index_columns AS ic
                        ON ic.object_id = i.object_id
                       AND ic.index_id = i.index_id
                       AND ic.key_ordinal > 0
                    WHERE i.object_id = @control
                      AND i.is_primary_key = 1
                ) = 1
                AND
                (
                    SELECT COUNT(*)
                    FROM sys.indexes AS i
                    INNER JOIN sys.index_columns AS ic
                        ON ic.object_id = i.object_id
                       AND ic.index_id = i.index_id
                       AND ic.key_ordinal > 0
                    INNER JOIN sys.columns AS c
                        ON c.object_id = ic.object_id
                       AND c.column_id = ic.column_id
                    WHERE i.object_id = @control
                      AND i.name = N'UQ_swapmid_etl_TargetResourceV1_ResourceInstanceId'
                      AND i.is_unique = 1
                      AND c.name = N'ResourceInstanceId'
                ) = 1
                AND
                (
                    SELECT COUNT(*)
                    FROM sys.indexes AS i
                    INNER JOIN sys.index_columns AS ic
                        ON ic.object_id = i.object_id
                       AND ic.index_id = i.index_id
                       AND ic.key_ordinal > 0
                    WHERE i.object_id = @control
                      AND i.name = N'UQ_swapmid_etl_TargetResourceV1_ResourceInstanceId'
                ) = 1
                AND
                (
                    SELECT COUNT(*)
                    FROM sys.indexes AS i
                    INNER JOIN sys.index_columns AS ic
                        ON ic.object_id = i.object_id
                       AND ic.index_id = i.index_id
                       AND ic.key_ordinal > 0
                    INNER JOIN sys.columns AS c
                        ON c.object_id = ic.object_id
                       AND c.column_id = ic.column_id
                    WHERE i.object_id = @receipt
                      AND i.is_primary_key = 1
                      AND c.name = N'RunId'
                ) = 1
                AND
                (
                    SELECT COUNT(*)
                    FROM sys.indexes AS i
                    INNER JOIN sys.index_columns AS ic
                        ON ic.object_id = i.object_id
                       AND ic.index_id = i.index_id
                       AND ic.key_ordinal > 0
                    WHERE i.object_id = @receipt
                      AND i.is_primary_key = 1
                ) = 1
                AND
                (
                    SELECT COUNT(*) FROM sys.foreign_keys
                    WHERE parent_object_id IN (@control, @receipt)
                ) = 1
                AND EXISTS
                (
                    SELECT 1
                    FROM sys.foreign_keys AS fk
                    INNER JOIN sys.foreign_key_columns AS fkc
                        ON fkc.constraint_object_id = fk.object_id
                    INNER JOIN sys.columns AS pc
                        ON pc.object_id = fkc.parent_object_id
                       AND pc.column_id = fkc.parent_column_id
                    INNER JOIN sys.columns AS rc
                        ON rc.object_id = fkc.referenced_object_id
                       AND rc.column_id = fkc.referenced_column_id
                    WHERE fk.name = N'FK_swapmid_etl_ExecutionReceiptV1_TargetResourceV1'
                      AND fk.parent_object_id = @receipt
                      AND fk.referenced_object_id = @control
                      AND fk.is_disabled = 0
                      AND fk.is_not_trusted = 0
                      AND fk.delete_referential_action = 0
                      AND fk.update_referential_action = 0
                      AND pc.name = N'ResourceInstanceId'
                      AND rc.name = N'ResourceInstanceId'
                )
                AND
                (
                    SELECT COUNT(*)
                    FROM sys.foreign_key_columns AS fkc
                    INNER JOIN sys.foreign_keys AS fk
                        ON fk.object_id = fkc.constraint_object_id
                    WHERE fk.parent_object_id = @receipt
                      AND fk.name = N'FK_swapmid_etl_ExecutionReceiptV1_TargetResourceV1'
                ) = 1
                AND
                (
                    SELECT COUNT(*) FROM sys.check_constraints
                    WHERE parent_object_id IN (@control, @receipt)
                      AND is_disabled = 0
                      AND is_not_trusted = 0
                      AND name IN
                      (
                          N'CK_swapmid_etl_TargetResourceV1_SingletonId',
                          N'CK_swapmid_etl_TargetResourceV1_ProtocolVersion',
                          N'CK_swapmid_etl_ExecutionReceiptV1_ProtocolVersion',
                          N'CK_swapmid_etl_ExecutionReceiptV1_WriteMode',
                          N'CK_swapmid_etl_ExecutionReceiptV1_ProcessedRows'
                      )
                ) = 5
                AND
                (
                    SELECT COUNT(*) FROM sys.check_constraints
                    WHERE parent_object_id IN (@control, @receipt)
                ) = 5
                AND
                (
                    SELECT COUNT(*) FROM sys.default_constraints
                    WHERE parent_object_id IN (@control, @receipt)
                ) = 2
                AND EXISTS
                (
                    SELECT 1
                    FROM sys.default_constraints AS dc
                    INNER JOIN sys.columns AS c
                        ON c.object_id = dc.parent_object_id
                       AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id = @control
                      AND dc.name = N'DF_swapmid_etl_TargetResourceV1_CreatedAtUtc'
                      AND c.name = N'CreatedAtUtc'
                      AND LOWER(REPLACE(REPLACE(dc.definition, N'(', N''), N')', N'')) = N'sysutcdatetime'
                )
                AND EXISTS
                (
                    SELECT 1
                    FROM sys.default_constraints AS dc
                    INNER JOIN sys.columns AS c
                        ON c.object_id = dc.parent_object_id
                       AND c.column_id = dc.parent_column_id
                    WHERE dc.parent_object_id = @receipt
                      AND dc.name = N'DF_swapmid_etl_ExecutionReceiptV1_RecordedAtUtc'
                      AND c.name = N'RecordedAtUtc'
                      AND LOWER(REPLACE(REPLACE(dc.definition, N'(', N''), N')', N'')) = N'sysutcdatetime'
                )
                THEN 1 ELSE 0 END);
            """;
        var contractSupported = await connection.QuerySingleAsync<bool>(
            new CommandDefinition(
                capabilitySql,
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: cancellationToken));
        if (!contractSupported)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        const string columnsSql = """
            SELECT
                t.name AS TableName,
                c.column_id AS OrdinalPosition,
                c.name AS ColumnName,
                ty.name AS DataType,
                CONVERT(int, c.max_length) AS MaxLength,
                CONVERT(int, c.precision) AS NumericPrecision,
                CONVERT(int, c.scale) AS NumericScale,
                c.is_nullable AS IsNullable,
                c.is_identity AS IsIdentity,
                c.is_computed AS IsComputed,
                c.is_rowguidcol AS IsRowGuidCol,
                c.is_sparse AS IsSparse,
                c.is_column_set AS IsColumnSet,
                c.is_masked AS IsMasked,
                c.encryption_type AS EncryptionType,
                c.generated_always_type AS GeneratedAlwaysType,
                c.is_hidden AS IsHidden
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            INNER JOIN sys.types AS ty ON ty.user_type_id = c.user_type_id
            WHERE s.name = N'swapmid_etl'
              AND t.name IN (N'TargetResourceV1', N'ExecutionReceiptV1')
            ORDER BY
                CASE t.name WHEN N'TargetResourceV1' THEN 0 ELSE 1 END,
                c.column_id;
            """;
        var columns = (await connection.QueryAsync<SqlServerColumnRow>(
            new CommandDefinition(
                columnsSql,
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
        if (columns.Length != SqlServerContractColumns.Length)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        for (var index = 0; index < columns.Length; index++)
        {
            var actual = columns[index];
            var expected = SqlServerContractColumns[index];
            if (!string.Equals(actual.TableName, expected.TableName, StringComparison.Ordinal) ||
                actual.OrdinalPosition != expected.OrdinalPosition ||
                !string.Equals(actual.ColumnName, expected.ColumnName, StringComparison.Ordinal) ||
                !string.Equals(actual.DataType, expected.DataType, StringComparison.OrdinalIgnoreCase) ||
                actual.MaxLength != expected.MaxLength ||
                actual.NumericPrecision != expected.NumericPrecision ||
                actual.NumericScale != expected.NumericScale ||
                actual.IsNullable ||
                actual.IsIdentity ||
                actual.IsComputed ||
                actual.IsRowGuidCol ||
                actual.IsSparse ||
                actual.IsColumnSet ||
                actual.IsMasked ||
                actual.EncryptionType != null ||
                actual.GeneratedAlwaysType != 0 ||
                actual.IsHidden)
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }
    }

    private sealed record SqlServerColumnShape(
        string TableName,
        int OrdinalPosition,
        string ColumnName,
        string DataType,
        int MaxLength,
        int NumericPrecision,
        int NumericScale);

    private sealed class SqlServerColumnRow
    {
        public string TableName { get; set; } = string.Empty;
        public int OrdinalPosition { get; set; }
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int MaxLength { get; set; }
        public int NumericPrecision { get; set; }
        public int NumericScale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
        public bool IsRowGuidCol { get; set; }
        public bool IsSparse { get; set; }
        public bool IsColumnSet { get; set; }
        public bool IsMasked { get; set; }
        public int? EncryptionType { get; set; }
        public int GeneratedAlwaysType { get; set; }
        public bool IsHidden { get; set; }
    }
}
