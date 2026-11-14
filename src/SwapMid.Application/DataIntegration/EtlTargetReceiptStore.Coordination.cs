using System;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

public sealed partial class EtlTargetReceiptStore
{
    private static async Task<string> EnsureCoordinationIdentityAsync(
        DbConnection connection,
        DbTransaction? transaction,
        RelationalDataSourceConnection.Probe probe,
        string? expectedHash,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (expectedHash != null && !IsSha256Hex(expectedHash))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var actualHash = await ReadCoordinationIdentityHashAsync(
            connection,
            transaction,
            probe,
            commandTimeoutSeconds,
            cancellationToken);
        if (expectedHash != null && !string.Equals(
                expectedHash,
                actualHash,
                StringComparison.Ordinal))
        {
            // 不暴露实例名、主机名、数据库名或配置值。节点漂移只能失败关闭并重新激活。
            throw EtlPipelineRunner.InvalidInput();
        }

        return actualHash;
    }

    private static async Task<string> ReadCoordinationIdentityHashAsync(
        DbConnection connection,
        DbTransaction? transaction,
        RelationalDataSourceConnection.Probe probe,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var provider = NormalizeProvider(probe.NormalizedProvider);
        if (connection is SqlConnection && provider == "sqlserver")
        {
            const string sql = """
                SELECT
                    CASE
                        WHEN DATABASEPROPERTYEX(DB_NAME(), 'Status') = 'ONLINE'
                         AND DATABASEPROPERTYEX(DB_NAME(), 'Updateability') = 'READ_WRITE'
                        THEN 1 ELSE 0
                    END AS IsWritable,
                    COALESCE(
                        CONVERT(nvarchar(256), SERVERPROPERTY('ServerName')),
                        CONCAT(
                            CONVERT(nvarchar(256), SERVERPROPERTY('MachineName')),
                            N'\',
                            COALESCE(
                                CONVERT(nvarchar(256), SERVERPROPERTY('InstanceName')),
                                N'MSSQLSERVER'))) AS ServerIdentity,
                    DB_NAME() AS DatabaseName
                """;
            var identity = await connection.QuerySingleAsync<SqlServerIdentity>(
                new CommandDefinition(
                    sql,
                    transaction: transaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: cancellationToken));
            if (identity.IsWritable != 1 ||
                string.IsNullOrWhiteSpace(identity.ServerIdentity) ||
                string.IsNullOrWhiteSpace(identity.DatabaseName))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            return EtlReceiptHashes.ComputeCoordinationIdentity(
                provider,
                identity.ServerIdentity.Trim().ToUpperInvariant(),
                identity.DatabaseName.Trim().ToUpperInvariant());
        }

        if (connection is MySqlConnection && provider == "mysql")
        {
            const string engineSql = """
                SELECT
                    VERSION() AS ServerVersion,
                    @@version_comment AS VersionComment
                """;
            var engine = await connection.QuerySingleAsync<MySqlEngineIdentity>(
                new CommandDefinition(
                    engineSql,
                    transaction: transaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: cancellationToken));
            var isMariaDb = ContainsMariaDb(engine.ServerVersion) ||
                            ContainsMariaDb(engine.VersionComment);
            return isMariaDb
                ? await ReadMariaDbCoordinationIdentityHashAsync(
                    connection,
                    transaction,
                    commandTimeoutSeconds,
                    cancellationToken)
                : await ReadMySqlCoordinationIdentityHashAsync(
                    connection,
                    transaction,
                    commandTimeoutSeconds,
                    cancellationToken);
        }

        if (connection is NpgsqlConnection && provider == "postgresql")
        {
            const string pgSql = """
                SELECT
                    (NOT pg_catalog.pg_is_in_recovery()) AS IsWritable,
                    (SELECT system_identifier::text FROM pg_catalog.pg_control_system()) AS SystemIdentifier,
                    (SELECT oid::text FROM pg_catalog.pg_database WHERE datname = current_database()) AS DatabaseOid,
                    current_database() AS DatabaseName
                """;
            var identity = await connection.QuerySingleAsync<PostgreSqlIdentity>(
                new CommandDefinition(
                    pgSql,
                    transaction: transaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: cancellationToken));
            if (!identity.IsWritable ||
                string.IsNullOrWhiteSpace(identity.SystemIdentifier) ||
                string.IsNullOrWhiteSpace(identity.DatabaseOid) ||
                string.IsNullOrWhiteSpace(identity.DatabaseName))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            return EtlReceiptHashes.ComputeCoordinationIdentity(
                "postgresql",
                identity.SystemIdentifier.Trim(),
                identity.DatabaseOid.Trim(),
                identity.DatabaseName.Trim());
        }

        throw EtlPipelineRunner.InvalidInput();
    }

    private static async Task<string> ReadMySqlCoordinationIdentityHashAsync(
        DbConnection connection,
        DbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
            const string sql = """
                SELECT
                    CAST(@@global.read_only AS CHAR) AS ReadOnlyValue,
                    CAST(@@global.super_read_only AS CHAR) AS SuperReadOnlyValue,
                    @@global.server_uuid AS ServerIdentity,
                    DATABASE() AS DatabaseName
                """;
            var identity = await connection.QuerySingleAsync<MySqlIdentity>(
                new CommandDefinition(
                    sql,
                    transaction: transaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: cancellationToken));
            if (!IsOff(identity.ReadOnlyValue) ||
                !IsOff(identity.SuperReadOnlyValue) ||
                string.IsNullOrWhiteSpace(identity.ServerIdentity) ||
                !Guid.TryParse(identity.ServerIdentity, out var serverUuid) ||
                serverUuid == Guid.Empty ||
                string.IsNullOrWhiteSpace(identity.DatabaseName))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            return EtlReceiptHashes.ComputeCoordinationIdentity(
                "mysql",
                serverUuid.ToString("N"),
                identity.DatabaseName.Trim());
    }

    private static async Task<string> ReadMariaDbCoordinationIdentityHashAsync(
        DbConnection connection,
        DbTransaction? transaction,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
            const string sql = """
                SELECT
                    CAST(@@global.read_only AS CHAR) AS ReadOnlyValue,
                    CAST(@@global.server_id AS CHAR) AS ServerId,
                    @@global.hostname AS HostName,
                    CAST(@@global.port AS CHAR) AS PortValue,
                    DATABASE() AS DatabaseName
                """;
            var identity = await connection.QuerySingleAsync<MariaDbIdentity>(
                new CommandDefinition(
                    sql,
                    transaction: transaction,
                    commandTimeout: commandTimeoutSeconds,
                    cancellationToken: cancellationToken));
            if (!IsOff(identity.ReadOnlyValue) ||
                !ulong.TryParse(
                    identity.ServerId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var serverId) ||
                serverId == 0 ||
                !int.TryParse(
                    identity.PortValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var port) ||
                port is < 1 or > 65_535 ||
                string.IsNullOrWhiteSpace(identity.HostName) ||
                string.IsNullOrWhiteSpace(identity.DatabaseName))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            return EtlReceiptHashes.ComputeCoordinationIdentity(
                "mariadb",
                serverId.ToString(CultureInfo.InvariantCulture),
                identity.HostName.Trim().ToUpperInvariant(),
                port.ToString(CultureInfo.InvariantCulture),
                identity.DatabaseName.Trim());
    }

    private static bool ContainsMariaDb(string? value) =>
        value?.Contains("MariaDB", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsOff(string? value) =>
        string.Equals(value?.Trim(), "0", StringComparison.Ordinal) ||
        string.Equals(value?.Trim(), "OFF", StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256Hex(string value) =>
        value.Length == PipelineExecutionConsts.Sha256HexLength &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private sealed class SqlServerIdentity
    {
        public int IsWritable { get; set; }
        public string? ServerIdentity { get; set; }
        public string? DatabaseName { get; set; }
    }

    private sealed class MySqlIdentity
    {
        public string? ReadOnlyValue { get; set; }
        public string? SuperReadOnlyValue { get; set; }
        public string? ServerIdentity { get; set; }
        public string? DatabaseName { get; set; }
    }

    private sealed class MySqlEngineIdentity
    {
        public string? ServerVersion { get; set; }
        public string? VersionComment { get; set; }
    }

    private sealed class MariaDbIdentity
    {
        public string? ReadOnlyValue { get; set; }
        public string? ServerId { get; set; }
        public string? HostName { get; set; }
        public string? PortValue { get; set; }
        public string? DatabaseName { get; set; }
    }

    private sealed class PostgreSqlIdentity
    {
        public bool IsWritable { get; set; }
        public string? SystemIdentifier { get; set; }
        public string? DatabaseOid { get; set; }
        public string? DatabaseName { get; set; }
    }
}
