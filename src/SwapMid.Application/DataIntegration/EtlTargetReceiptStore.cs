using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Npgsql;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// 固定目标对象的 receipt-v1 访问边界。永久对象只能由 DBA 预配；应用仅 SELECT/INSERT，
/// 不执行 CREATE/ALTER/DELETE/UPDATE。
/// </summary>
public sealed partial class EtlTargetReceiptStore :
    IEtlTargetReceiptStore,
    ITransientDependency
{
    internal const short ProtocolVersion = 1;
    internal const string SqlServerControlTable =
        "swapmid_etl.TargetResourceV1";
    internal const string SqlServerReceiptTable =
        "swapmid_etl.ExecutionReceiptV1";
    internal const string MySqlControlTable =
        "swapmid_etl_target_resource_v1";
    internal const string MySqlReceiptTable =
        "swapmid_etl_execution_receipt_v1";
    internal const string PostgreSqlControlTable =
        "swapmid_etl.target_resource_v1";
    internal const string PostgreSqlReceiptTable =
        "swapmid_etl.execution_receipt_v1";

    private const int ConnectTimeoutSeconds = 15;
    private const int CommandTimeoutSeconds = 60;
    private const int CleanupTimeoutSeconds = 10;

    private readonly IEtlRelationalConnectionFactory _connectionFactory;
    private readonly ILogger _logger;

    public EtlTargetReceiptStore(
        IEtlRelationalConnectionFactory connectionFactory,
        ILogger<EtlTargetReceiptStore>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger ?? NullLogger<EtlTargetReceiptStore>.Instance;
    }

    public async Task<EtlTargetReceiptActivation> ProbeActivationAsync(
        DataSource target,
        EtlTransformConfig transform,
        CancellationToken cancellationToken)
    {
        var probe = ParseSupportedTarget(target.Config);
        EnsureReceiptModeSupported(probe, transform);
        await using var connection = _connectionFactory.CreateTarget(
            target.Config,
            ConnectTimeoutSeconds,
            CommandTimeoutSeconds);
        await connection.OpenAsync(cancellationToken);
        await EtlProviderSession.ConfigureAsync(
            connection,
            probe,
            CommandTimeoutSeconds,
            cancellationToken);
        var targetCoordinationIdentityHash =
            await EnsureCoordinationIdentityAsync(
                connection,
                transaction: null,
                probe,
                expectedHash: null,
                CommandTimeoutSeconds,
                cancellationToken);

        var canonicalTargetTable =
            await EtlTargetSession.ResolveCanonicalTargetTableAsync(
                connection,
                transform.TargetTable,
                probe,
                CommandTimeoutSeconds,
                cancellationToken);
        EnsureBusinessTargetIsNotReserved(
            canonicalTargetTable,
            probe,
            connection.Database);
        await EnsureContractShapeAsync(
            connection,
            transaction: null,
            probe,
            CommandTimeoutSeconds,
            cancellationToken);
        var instanceId = await ReadResourceInstanceIdAsync(
            connection,
            transaction: null,
            probe,
            CommandTimeoutSeconds,
            cancellationToken);

        await ProbeRollbackAsync(
            connection,
            probe,
            instanceId,
            targetCoordinationIdentityHash,
            cancellationToken);
        await ProbeRejectedConstraintAsync(
            connection,
            probe,
            instanceId,
            protocolVersion: checked((short)(ProtocolVersion + 1)),
            writeMode: 1,
            processedRows: 0,
            cancellationToken: cancellationToken);
        await ProbeRejectedConstraintAsync(
            connection,
            probe,
            instanceId,
            protocolVersion: ProtocolVersion,
            writeMode: probe.NormalizedProvider is "mysql" or "mariadb"
                ? 2
                : 0,
            processedRows: 0,
            cancellationToken: cancellationToken);
        await ProbeRejectedConstraintAsync(
            connection,
            probe,
            instanceId,
            protocolVersion: ProtocolVersion,
            writeMode: 1,
            processedRows: -1,
            cancellationToken: cancellationToken);
        var provider = NormalizeProvider(probe.NormalizedProvider);
        return new EtlTargetReceiptActivation(
            instanceId,
            provider,
            canonicalTargetTable,
            EtlReceiptHashes.ComputeTargetResource(
                provider,
                instanceId,
                canonicalTargetTable),
            targetCoordinationIdentityHash);
    }

    public async Task<EtlTargetReceiptEvidence?> FindAsync(
        DataSource target,
        EtlExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (executionContext.TargetReceipt == null ||
            string.IsNullOrWhiteSpace(
                executionContext.TargetCoordinationIdentityHash))
        {
            return null;
        }

        var probe = ParseSupportedTarget(target.Config);
        EnsureReceiptWriteModeSupported(
            probe,
            executionContext.WriteMode);
        await using var connection = _connectionFactory.CreateTarget(
            target.Config,
            ConnectTimeoutSeconds,
            CommandTimeoutSeconds);
        await connection.OpenAsync(cancellationToken);
        await EtlProviderSession.ConfigureAsync(
            connection,
            probe,
            CommandTimeoutSeconds,
            cancellationToken);
        await EnsureCoordinationIdentityAsync(
            connection,
            transaction: null,
            probe,
            executionContext.TargetCoordinationIdentityHash,
            CommandTimeoutSeconds,
            cancellationToken);
        await EnsureContractShapeAsync(
            connection,
            transaction: null,
            probe,
            CommandTimeoutSeconds,
            cancellationToken);
        var instanceId = await ReadResourceInstanceIdAsync(
            connection,
            transaction: null,
            probe,
            CommandTimeoutSeconds,
            cancellationToken);
        if (instanceId != executionContext.TargetReceipt.ResourceInstanceId)
        {
            // 连接已落到另一个逻辑目标，不能把身份错配降格成“该 RunId 不存在”。
            // 上层据此记录 mismatch critical，同时仍保持 OutcomeUnknown。
            throw EtlPipelineRunner.InvalidInput();
        }

        var canonicalTarget =
            await EtlTargetSession.ResolveCanonicalTargetTableAsync(
                connection,
                executionContext.CanonicalTargetTable,
                probe,
                CommandTimeoutSeconds,
                cancellationToken);
        EnsureBusinessTargetIsNotReserved(
            canonicalTarget,
            probe,
            connection.Database);
        var provider = NormalizeProvider(probe.NormalizedProvider);
        if (!string.Equals(
                canonicalTarget,
                executionContext.CanonicalTargetTable,
                StringComparison.Ordinal) ||
            !string.Equals(
                EtlReceiptHashes.ComputeTargetResource(
                    provider,
                    instanceId,
                    canonicalTarget),
                executionContext.VerifiedTargetResourceHash,
                StringComparison.Ordinal))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return await ReadReceiptAsync(
            connection,
            transaction: null,
            probe,
            executionContext.RunId,
            forUpdate: false,
            CommandTimeoutSeconds,
            cancellationToken);
    }

    public async Task<EtlTargetReceiptInsertResult> InsertOrReadAsync(
        DbConnection connection,
        DbTransaction transaction,
        EtlTargetReceiptProviderContext providerContext,
        EtlTargetReceiptEvidence evidence,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var targetProbe = new RelationalDataSourceConnection.Probe
        {
            Provider = providerContext.Provider,
            Database = providerContext.Database,
        };
        var isSqlServer =
            connection is SqlConnection &&
            transaction is SqlTransaction &&
            targetProbe.NormalizedProvider is "sqlserver" or "mssql";
        var isPostgreSql =
            connection is NpgsqlConnection &&
            transaction is NpgsqlTransaction &&
            targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql";
        if ((!isSqlServer && !isPostgreSql) ||
            !ReferenceEquals(transaction.Connection, connection) ||
            string.IsNullOrWhiteSpace(providerContext.Database) ||
            string.IsNullOrWhiteSpace(
                providerContext.TargetCoordinationIdentityHash) ||
            !string.Equals(
                connection.Database,
                providerContext.Database,
                StringComparison.Ordinal))
        {
            throw EtlPipelineRunner.InvalidInput();
        }
        EnsureReceiptWriteModeSupported(targetProbe, evidence.WriteMode);
        await EnsureCoordinationIdentityAsync(
            connection,
            transaction,
            targetProbe,
            providerContext.TargetCoordinationIdentityHash,
            commandTimeoutSeconds,
            cancellationToken);
        await EnsureContractShapeAsync(
            connection,
            transaction,
            targetProbe,
            commandTimeoutSeconds,
            cancellationToken);
        var instanceId = await ReadResourceInstanceIdAsync(
            connection,
            transaction,
            targetProbe,
            commandTimeoutSeconds,
            cancellationToken);
        if (instanceId != evidence.ResourceInstanceId)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var existing = await ReadReceiptAsync(
            connection,
            transaction,
            targetProbe,
            evidence.RunId,
            forUpdate: true,
            commandTimeoutSeconds,
            cancellationToken);
        if (existing != null)
        {
            return ReceiptEquals(existing, evidence)
                ? EtlTargetReceiptInsertResult.ExistingExact
                : EtlTargetReceiptInsertResult.ExistingMismatch;
        }

        try
        {
            await InsertAsync(
                connection,
                transaction,
                targetProbe,
                evidence,
                commandTimeoutSeconds,
                cancellationToken);
        }
        catch (MySqlException exception) when (
            connection is MySqlConnection &&
            targetProbe.NormalizedProvider is "mysql" or "mariadb" &&
            exception.Number == 1062)
        {
            // MySQL ReadCommitted 下 absent-key FOR UPDATE 不保证 gap lock。另一个
            // target lease 可能以同一 RunId 并发提交 receipt，使本次 INSERT 在未触碰
            // 业务表前命中 PK。此时必须重读并交给上层按 exact/mismatch 进入未知态，
            // 绝不能把 1062 当普通写失败收敛为 Failed。
            EtlTargetReceiptEvidence? concurrentReceipt;
            try
            {
                concurrentReceipt = await ReadReceiptAsync(
                    connection,
                    transaction,
                    targetProbe,
                    evidence.RunId,
                    forUpdate: true,
                    commandTimeoutSeconds,
                    cancellationToken);
            }
            catch
            {
                // 已观察到目标回执主键冲突后，取消/网络错误同样不能证明该 RunId
                // 未在另一事务应用；固定升级为 OutcomeUnknown，且不携带 provider 原文。
                throw new EtlTargetCommitOutcomeUnknownException();
            }

            if (concurrentReceipt == null)
            {
                throw new EtlTargetCommitOutcomeUnknownException();
            }

            return ReceiptEquals(concurrentReceipt, evidence)
                ? EtlTargetReceiptInsertResult.ExistingExact
                : EtlTargetReceiptInsertResult.ExistingMismatch;
        }
        catch (PostgresException exception) when (
            connection is NpgsqlConnection &&
            targetProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql" &&
            exception.SqlState == "23505")
        {
            throw new EtlTargetCommitOutcomeUnknownException();
        }
        var inserted = await ReadReceiptAsync(
            connection,
            transaction,
            targetProbe,
            evidence.RunId,
            forUpdate: false,
            commandTimeoutSeconds,
            cancellationToken);
        if (inserted == null || !ReceiptEquals(inserted, evidence))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return EtlTargetReceiptInsertResult.Inserted;
    }

    private async Task ProbeRollbackAsync(
        DbConnection connection,
        RelationalDataSourceConnection.Probe probe,
        Guid instanceId,
        string targetCoordinationIdentityHash,
        CancellationToken cancellationToken)
    {
        var evidence = CreateProbeEvidence(instanceId);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            var result = await InsertOrReadAsync(
                connection,
                transaction,
                new EtlTargetReceiptProviderContext(
                    probe.NormalizedProvider,
                    connection.Database,
                    targetCoordinationIdentityHash),
                evidence,
                CommandTimeoutSeconds,
                cancellationToken);
            if (result != EtlTargetReceiptInsertResult.Inserted)
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(
                TimeSpan.FromSeconds(CleanupTimeoutSeconds));
            await transaction.RollbackAsync(cleanup.Token);
        }

        var residue = await ReadReceiptAsync(
            connection,
            transaction: null,
            probe,
            evidence.RunId,
            forUpdate: false,
            CommandTimeoutSeconds,
            cancellationToken);
        if (residue != null)
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    private static async Task ProbeRejectedConstraintAsync(
        DbConnection connection,
        RelationalDataSourceConnection.Probe probe,
        Guid instanceId,
        short protocolVersion,
        int writeMode,
        long processedRows,
        CancellationToken cancellationToken)
    {
        var evidence = CreateProbeEvidence(instanceId, processedRows);
        var rejectedByCheckConstraint = false;
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        try
        {
            try
            {
                await InsertValuesAsync(
                    connection,
                    transaction,
                    probe,
                    evidence,
                    protocolVersion,
                    writeMode,
                    CommandTimeoutSeconds,
                    cancellationToken);
            }
            catch (SqlException exception) when (
                probe.NormalizedProvider is "sqlserver" or "mssql" &&
                exception.Number == 547)
            {
                rejectedByCheckConstraint = true;
            }
            catch (MySqlException exception) when (
                probe.NormalizedProvider is "mysql" or "mariadb" &&
                exception.Number is 3819 or 4025)
            {
                rejectedByCheckConstraint = true;
            }
            catch (PostgresException exception) when (
                probe.NormalizedProvider is "postgresql" or "postgres" or "npgsql" &&
                exception.SqlState == "23514")
            {
                rejectedByCheckConstraint = true;
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(
                TimeSpan.FromSeconds(CleanupTimeoutSeconds));
            await transaction.RollbackAsync(cleanup.Token);
        }

        if (!rejectedByCheckConstraint)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var residue = await ReadReceiptAsync(
            connection,
            transaction: null,
            probe,
            evidence.RunId,
            forUpdate: false,
            CommandTimeoutSeconds,
            cancellationToken);
        if (residue != null)
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }

    private static EtlTargetReceiptEvidence CreateProbeEvidence(
        Guid instanceId,
        long processedRows = 0) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            instanceId,
            new string('A', PipelineExecutionConsts.Sha256HexLength),
            new string('B', PipelineExecutionConsts.Sha256HexLength),
            new string('C', PipelineExecutionConsts.Sha256HexLength),
            new string('D', PipelineExecutionConsts.Sha256HexLength),
            new string('E', PipelineExecutionConsts.Sha256HexLength),
            new string('F', PipelineExecutionConsts.Sha256HexLength),
            EtlWriteMode.Append,
            processedRows);

}
