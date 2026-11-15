using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

public sealed partial class EtlTargetReceiptStore
{
    private static async Task EnsureContractShapeAsync(
        DbConnection connection,
        DbTransaction? transaction,
        RelationalDataSourceConnection.Probe probe,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var sqlServer = connection is SqlConnection &&
                        probe.NormalizedProvider is "sqlserver" or "mssql";
        var mySql = connection is MySqlConnection &&
                    probe.NormalizedProvider is "mysql" or "mariadb";
        var postgreSql = connection is NpgsqlConnection &&
                         probe.NormalizedProvider is "postgresql" or "postgres" or "npgsql";
        if (!sqlServer && !mySql && !postgreSql)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        if (sqlServer)
        {
            await EnsureSqlServerContractShapeAsync(
                connection,
                transaction,
                commandTimeoutSeconds,
                cancellationToken);
            return;
        }

        if (postgreSql)
        {
            await EnsurePostgreSqlContractShapeAsync(
                connection,
                transaction,
                commandTimeoutSeconds,
                cancellationToken);
            return;
        }

        await EnsureMySqlContractShapeAsync(
            connection,
            transaction,
            commandTimeoutSeconds,
            cancellationToken);
    }

    private static async Task<Guid> ReadResourceInstanceIdAsync(
        DbConnection connection,
        DbTransaction? transaction,
        RelationalDataSourceConnection.Probe probe,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var sqlServer = connection is SqlConnection &&
                        probe.NormalizedProvider is "sqlserver" or "mssql";
        var postgreSql = connection is NpgsqlConnection &&
                         probe.NormalizedProvider is "postgresql" or "postgres" or "npgsql";
        var nativeGuid = sqlServer || postgreSql;
        var sql = sqlServer
            ? $"""
               SELECT [SingletonId], [ProtocolVersion], [ResourceInstanceId]
               FROM [swapmid_etl].[TargetResourceV1]
               {(transaction == null ? string.Empty : "WITH (UPDLOCK, HOLDLOCK)")}
               """
            : postgreSql
                ? $"""
                   SELECT "singleton_id", "protocol_version", "resource_instance_id"
                   FROM "swapmid_etl"."target_resource_v1"
                   {(transaction == null ? string.Empty : "FOR UPDATE")}
                   """
                : $"""
                   SELECT `singleton_id`, `protocol_version`, `resource_instance_id`
                   FROM `swapmid_etl_target_resource_v1`
                   {(transaction == null ? string.Empty : "FOR UPDATE")}
                   """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Transaction = transaction;
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            Convert.ToInt32(reader.GetValue(0)) != 1 ||
            Convert.ToInt32(reader.GetValue(1)) != ProtocolVersion)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var instanceId = ReadGuid(reader.GetValue(2), nativeGuid);
        if (instanceId == Guid.Empty ||
            await reader.ReadAsync(cancellationToken))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return instanceId;
    }

    private static async Task<EtlTargetReceiptEvidence?> ReadReceiptAsync(
        DbConnection connection,
        DbTransaction? transaction,
        RelationalDataSourceConnection.Probe probe,
        Guid runId,
        bool forUpdate,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var sqlServer = connection is SqlConnection &&
                        probe.NormalizedProvider is "sqlserver" or "mssql";
        var postgreSql = connection is NpgsqlConnection &&
                         probe.NormalizedProvider is "postgresql" or "postgres" or "npgsql";
        var nativeGuid = sqlServer || postgreSql;
        var lockClause = !forUpdate
            ? string.Empty
            : sqlServer
                ? "WITH (UPDLOCK, HOLDLOCK)"
                : "FOR UPDATE";
        var sql = sqlServer
            ? $"""
               SELECT [RunId], [ProtocolVersion], [ResourceInstanceId], [PipelineId],
                      [LegacyTargetResourceHash], [VerifiedTargetResourceHash],
                      [ExecutionContractHash], [SourceQueryHash], [TargetSchemaHash],
                      [BatchDigest], [WriteMode], [ProcessedRows]
               FROM [swapmid_etl].[ExecutionReceiptV1] {lockClause}
               WHERE [RunId] = @runId
               """
            : postgreSql
                ? $"""
                   SELECT "run_id", "protocol_version", "resource_instance_id", "pipeline_id",
                          "legacy_target_resource_hash", "verified_target_resource_hash",
                          "execution_contract_hash", "source_query_hash", "target_schema_hash",
                          "batch_digest", "write_mode", "processed_rows"
                   FROM "swapmid_etl"."execution_receipt_v1"
                   WHERE "run_id" = @runId {lockClause}
                   """
                : $"""
                   SELECT `run_id`, `protocol_version`, `resource_instance_id`, `pipeline_id`,
                          `legacy_target_resource_hash`, `verified_target_resource_hash`,
                          `execution_contract_hash`, `source_query_hash`, `target_schema_hash`,
                          `batch_digest`, `write_mode`, `processed_rows`
                   FROM `swapmid_etl_execution_receipt_v1`
                   WHERE `run_id` = @runId {lockClause}
                   """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Transaction = transaction;
        AddGuidParameter(command, "runId", runId, nativeGuid);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        if (Convert.ToInt32(reader.GetValue(1)) != ProtocolVersion)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var evidence = new EtlTargetReceiptEvidence(
            ReadGuid(reader.GetValue(0), nativeGuid),
            ReadGuid(reader.GetValue(3), nativeGuid),
            ReadGuid(reader.GetValue(2), nativeGuid),
            ReadHash(reader.GetValue(4)),
            ReadHash(reader.GetValue(5)),
            ReadHash(reader.GetValue(6)),
            ReadHash(reader.GetValue(7)),
            ReadHash(reader.GetValue(8)),
            ReadHash(reader.GetValue(9)),
            Convert.ToInt32(reader.GetValue(10)) switch
            {
                1 => EtlWriteMode.Append,
                2 => EtlWriteMode.Truncate,
                3 => EtlWriteMode.Upsert,
                _ => throw EtlPipelineRunner.InvalidInput(),
            },
            Convert.ToInt64(reader.GetValue(11)));
        if (evidence.ProcessedRows < 0 ||
            await reader.ReadAsync(cancellationToken))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return evidence;
    }

    private static Task InsertAsync(
        DbConnection connection,
        DbTransaction transaction,
        RelationalDataSourceConnection.Probe probe,
        EtlTargetReceiptEvidence evidence,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken) =>
        InsertValuesAsync(
            connection,
            transaction,
            probe,
            evidence,
            ProtocolVersion,
            EtlReceiptHashes.EncodeWriteMode(evidence.WriteMode),
            commandTimeoutSeconds,
            cancellationToken);

    private static async Task InsertValuesAsync(
        DbConnection connection,
        DbTransaction transaction,
        RelationalDataSourceConnection.Probe probe,
        EtlTargetReceiptEvidence evidence,
        short protocolVersion,
        int writeMode,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var sqlServer = connection is SqlConnection &&
                        probe.NormalizedProvider is "sqlserver" or "mssql";
        var postgreSql = connection is NpgsqlConnection &&
                         probe.NormalizedProvider is "postgresql" or "postgres" or "npgsql";
        var nativeGuid = sqlServer || postgreSql;
        var sql = sqlServer
            ? """
              INSERT INTO [swapmid_etl].[ExecutionReceiptV1]
                  ([RunId], [ProtocolVersion], [ResourceInstanceId], [PipelineId],
                   [LegacyTargetResourceHash], [VerifiedTargetResourceHash],
                   [ExecutionContractHash], [SourceQueryHash], [TargetSchemaHash],
                   [BatchDigest], [WriteMode], [ProcessedRows])
              VALUES
                  (@runId, @protocolVersion, @resourceInstanceId, @pipelineId,
                   @legacyTargetResourceHash, @verifiedTargetResourceHash,
                   @executionContractHash, @sourceQueryHash, @targetSchemaHash,
                   @batchDigest, @writeMode, @processedRows)
              """
            : postgreSql
                ? """
                  INSERT INTO "swapmid_etl"."execution_receipt_v1"
                      ("run_id", "protocol_version", "resource_instance_id", "pipeline_id",
                       "legacy_target_resource_hash", "verified_target_resource_hash",
                       "execution_contract_hash", "source_query_hash", "target_schema_hash",
                       "batch_digest", "write_mode", "processed_rows")
                  VALUES
                      (@runId, @protocolVersion, @resourceInstanceId, @pipelineId,
                       @legacyTargetResourceHash, @verifiedTargetResourceHash,
                       @executionContractHash, @sourceQueryHash, @targetSchemaHash,
                       @batchDigest, @writeMode, @processedRows)
                  """
                : """
                  INSERT INTO `swapmid_etl_execution_receipt_v1`
                      (`run_id`, `protocol_version`, `resource_instance_id`, `pipeline_id`,
                       `legacy_target_resource_hash`, `verified_target_resource_hash`,
                       `execution_contract_hash`, `source_query_hash`, `target_schema_hash`,
                       `batch_digest`, `write_mode`, `processed_rows`, `recorded_at_utc`)
                  VALUES
                      (@runId, @protocolVersion, @resourceInstanceId, @pipelineId,
                       @legacyTargetResourceHash, @verifiedTargetResourceHash,
                       @executionContractHash, @sourceQueryHash, @targetSchemaHash,
                       @batchDigest, @writeMode, @processedRows, UTC_TIMESTAMP(6))
                  """;
        var parameters = new DynamicParameters();
        AddGuidParameter(parameters, "runId", evidence.RunId, nativeGuid);
        parameters.Add("protocolVersion", protocolVersion, DbType.Int16);
        AddGuidParameter(
            parameters,
            "resourceInstanceId",
            evidence.ResourceInstanceId,
            nativeGuid);
        AddGuidParameter(
            parameters,
            "pipelineId",
            evidence.PipelineId,
            nativeGuid);
        AddHashParameter(
            parameters,
            "legacyTargetResourceHash",
            evidence.LegacyTargetResourceHash);
        AddHashParameter(
            parameters,
            "verifiedTargetResourceHash",
            evidence.VerifiedTargetResourceHash);
        AddHashParameter(
            parameters,
            "executionContractHash",
            evidence.ExecutionContractHash);
        AddHashParameter(
            parameters,
            "sourceQueryHash",
            evidence.SourceQueryHash);
        AddHashParameter(
            parameters,
            "targetSchemaHash",
            evidence.TargetSchemaHash);
        AddHashParameter(parameters, "batchDigest", evidence.BatchDigest);
        if (postgreSql)
        {
            parameters.Add("writeMode", checked((short)writeMode), DbType.Int16);
        }
        else
        {
            parameters.Add("writeMode", writeMode, DbType.Byte);
        }
        parameters.Add("processedRows", evidence.ProcessedRows, DbType.Int64);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            transaction,
            commandTimeoutSeconds,
            cancellationToken: cancellationToken));
    }

    private static object ToDbGuid(Guid value, bool nativeGuid) =>
        nativeGuid ? value : ToRfc4122Bytes(value);

    private static Guid ReadGuid(object value, bool nativeGuid)
    {
        if (nativeGuid && value is Guid guid)
        {
            return guid;
        }

        if (value is byte[] { Length: 16 } bytes)
        {
            return FromRfc4122Bytes(bytes);
        }

        throw EtlPipelineRunner.InvalidInput();
    }

    private static string ReadHash(object value)
    {
        if (value is not byte[] { Length: 32 } bytes)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return Convert.ToHexString(bytes);
    }

    private static void AddGuidParameter(
        DbCommand command,
        string name,
        Guid value,
        bool nativeGuid)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = nativeGuid ? DbType.Guid : DbType.Binary;
        if (!nativeGuid)
        {
            parameter.Size = 16;
        }

        parameter.Value = ToDbGuid(value, nativeGuid);
        command.Parameters.Add(parameter);
    }

    private static void AddGuidParameter(
        DynamicParameters parameters,
        string name,
        Guid value,
        bool nativeGuid) =>
        parameters.Add(
            name,
            ToDbGuid(value, nativeGuid),
            nativeGuid ? DbType.Guid : DbType.Binary,
            size: nativeGuid ? null : 16);

    private static void AddHashParameter(
        DynamicParameters parameters,
        string name,
        string value) =>
        parameters.Add(
            name,
            Convert.FromHexString(value),
            DbType.Binary,
            size: 32);
}
