using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace SwapMid.DataIntegration;

public sealed partial class EtlTargetReceiptStore
{
    private static readonly PostgreSqlColumnShape[] PostgreSqlContractColumns =
    [
        new("target_resource_v1", 1, "singleton_id", "smallint"),
        new("target_resource_v1", 2, "protocol_version", "smallint"),
        new("target_resource_v1", 3, "resource_instance_id", "uuid"),
        new("target_resource_v1", 4, "created_at_utc", "timestamp with time zone", HasDefault: true),
        new("execution_receipt_v1", 1, "run_id", "uuid"),
        new("execution_receipt_v1", 2, "protocol_version", "smallint"),
        new("execution_receipt_v1", 3, "resource_instance_id", "uuid"),
        new("execution_receipt_v1", 4, "pipeline_id", "uuid"),
        new("execution_receipt_v1", 5, "legacy_target_resource_hash", "bytea"),
        new("execution_receipt_v1", 6, "verified_target_resource_hash", "bytea"),
        new("execution_receipt_v1", 7, "execution_contract_hash", "bytea"),
        new("execution_receipt_v1", 8, "source_query_hash", "bytea"),
        new("execution_receipt_v1", 9, "target_schema_hash", "bytea"),
        new("execution_receipt_v1", 10, "batch_digest", "bytea"),
        new("execution_receipt_v1", 11, "write_mode", "smallint"),
        new("execution_receipt_v1", 12, "processed_rows", "bigint"),
        new("execution_receipt_v1", 13, "recorded_at_utc", "timestamp with time zone", HasDefault: true),
    ];

    private static async Task EnsurePostgreSqlContractShapeAsync(
        DbConnection connection,
        DbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        const string capabilitySql = """
            SELECT (
                current_setting('server_version_num')::int >= 120000
                AND NOT pg_catalog.pg_is_in_recovery()
                AND (
                    SELECT COUNT(*)
                    FROM pg_catalog.pg_class AS c
                    INNER JOIN pg_catalog.pg_namespace AS n
                        ON n.oid = c.relnamespace
                    WHERE n.nspname = 'swapmid_etl'
                      AND c.relname IN ('target_resource_v1', 'execution_receipt_v1')
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
                      )
                ) = 2
                AND has_table_privilege('swapmid_etl.target_resource_v1', 'SELECT')
                AND NOT has_table_privilege('swapmid_etl.target_resource_v1', 'INSERT')
                AND NOT has_table_privilege('swapmid_etl.target_resource_v1', 'UPDATE')
                AND NOT has_table_privilege('swapmid_etl.target_resource_v1', 'DELETE')
                AND NOT has_table_privilege('swapmid_etl.target_resource_v1', 'TRUNCATE')
                AND has_table_privilege('swapmid_etl.execution_receipt_v1', 'SELECT')
                AND has_table_privilege('swapmid_etl.execution_receipt_v1', 'INSERT')
                AND NOT has_table_privilege('swapmid_etl.execution_receipt_v1', 'UPDATE')
                AND NOT has_table_privilege('swapmid_etl.execution_receipt_v1', 'DELETE')
                AND NOT has_table_privilege('swapmid_etl.execution_receipt_v1', 'TRUNCATE')
                AND (
                    SELECT COUNT(*)
                    FROM pg_catalog.pg_constraint AS con
                    INNER JOIN pg_catalog.pg_class AS c ON c.oid = con.conrelid
                    INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'swapmid_etl'
                      AND c.relname IN ('target_resource_v1', 'execution_receipt_v1')
                      AND con.convalidated
                      AND con.conname IN (
                          'pk_swapmid_etl_target_resource_v1',
                          'uq_swapmid_etl_target_resource_v1_resource_instance_id',
                          'pk_swapmid_etl_execution_receipt_v1',
                          'fk_swapmid_etl_execution_receipt_v1_target_resource_v1',
                          'ck_swapmid_etl_target_resource_v1_singleton_id',
                          'ck_swapmid_etl_target_resource_v1_protocol_version',
                          'ck_swapmid_etl_execution_receipt_v1_protocol_version',
                          'ck_swapmid_etl_execution_receipt_v1_write_mode',
                          'ck_swapmid_etl_execution_receipt_v1_processed_rows'
                      )
                ) = 9
            )
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
                c.relname AS TableName,
                a.attnum::int AS OrdinalPosition,
                a.attname AS ColumnName,
                pg_catalog.format_type(a.atttypid, a.atttypmod) AS FormattedType,
                a.attnotnull AS IsNotNull,
                a.attidentity::text AS IdentityKind,
                a.attgenerated::text AS GeneratedKind,
                (ad.adbin IS NOT NULL) AS HasDefault
            FROM pg_catalog.pg_attribute AS a
            INNER JOIN pg_catalog.pg_class AS c ON c.oid = a.attrelid
            INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            LEFT JOIN pg_catalog.pg_attrdef AS ad
                ON ad.adrelid = a.attrelid
               AND ad.adnum = a.attnum
            WHERE n.nspname = 'swapmid_etl'
              AND c.relname IN ('target_resource_v1', 'execution_receipt_v1')
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY
                CASE c.relname WHEN 'target_resource_v1' THEN 0 ELSE 1 END,
                a.attnum
            """;

        var columns = (await connection.QueryAsync<PostgreSqlColumnRow>(
            new CommandDefinition(
                columnsSql,
                transaction: transaction,
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: cancellationToken))).ToArray();
        if (columns.Length != PostgreSqlContractColumns.Length)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        for (var index = 0; index < columns.Length; index++)
        {
            var actual = columns[index];
            var expected = PostgreSqlContractColumns[index];
            if (!string.Equals(actual.TableName, expected.TableName, StringComparison.Ordinal) ||
                actual.OrdinalPosition != expected.OrdinalPosition ||
                !string.Equals(actual.ColumnName, expected.ColumnName, StringComparison.Ordinal) ||
                !string.Equals(actual.FormattedType, expected.FormattedType, StringComparison.Ordinal) ||
                !actual.IsNotNull ||
                !string.IsNullOrEmpty(actual.IdentityKind) ||
                !string.IsNullOrEmpty(actual.GeneratedKind) ||
                actual.HasDefault != expected.HasDefault)
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }
    }

    private sealed record PostgreSqlColumnShape(
        string TableName,
        int OrdinalPosition,
        string ColumnName,
        string FormattedType,
        bool HasDefault = false);

    private sealed class PostgreSqlColumnRow
    {
        public string TableName { get; set; } = string.Empty;
        public int OrdinalPosition { get; set; }
        public string ColumnName { get; set; } = string.Empty;
        public string FormattedType { get; set; } = string.Empty;
        public bool IsNotNull { get; set; }
        public string IdentityKind { get; set; } = string.Empty;
        public string GeneratedKind { get; set; } = string.Empty;
        public bool HasDefault { get; set; }
    }
}
