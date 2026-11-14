using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;


public sealed partial class DataPipelineExecutionCoordinator
{

    public const string TargetResourcePropertyName =
        "PipelineExecution.TargetResource";
    public const string TargetDataSourceIdPropertyName =
        "PipelineExecution.TargetDataSourceId";
    public const string TargetDataSourceNamePropertyName =
        "PipelineExecution.TargetDataSourceName";
    public const string TargetProviderPropertyName =
        "PipelineExecution.TargetProvider";
    public const string TargetEndpointPropertyName =
        "PipelineExecution.TargetEndpoint";
    public const string TargetDatabasePropertyName =
        "PipelineExecution.TargetDatabase";
    public const string TargetTablePropertyName =
        "PipelineExecution.TargetTable";
    public const string TargetConfigVersionPropertyName =
        "PipelineExecution.TargetConfigVersion";
    public const string TargetResourceGroupPropertyName =
        "PipelineExecution.TargetResourceGroup";
    public const string SourceDataSourceIdPropertyName =
        "PipelineExecution.SourceDataSourceId";
    public const string SourceDataSourceNamePropertyName =
        "PipelineExecution.SourceDataSourceName";
    public const string SourceConfigVersionPropertyName =
        "PipelineExecution.SourceConfigVersion";
    public const string WriteModePropertyName =
        "PipelineExecution.WriteMode";
    public const string SourceQuerySha256PropertyName =
        "PipelineExecution.SourceQuerySha256";
    public const string TargetIdentityVersionPropertyName =
        "PipelineExecution.TargetIdentityVersion";
    public const string ReceiptTargetSchemaHashPropertyName =
        "PipelineExecution.ReceiptTargetSchemaHash";
    public const string ReceiptBatchDigestPropertyName =
        "PipelineExecution.ReceiptBatchDigest";
    public const string ReceiptPreparedRowsPropertyName =
        "PipelineExecution.ReceiptPreparedRows";

    private static void StoreExecutionSnapshot(
        PipelineExecution execution,
        DataSource source,
        DataSource target,
        TargetIdentity identity,
        string canonicalTargetTable,
        bool receiptEnabled)
    {
        execution.SetProperty(
            TargetDataSourceIdPropertyName,
            target.Id.ToString("N"));
        execution.SetProperty(
            TargetDataSourceNamePropertyName,
            NormalizeSnapshotValue(target.Name, 128));
        execution.SetProperty(TargetProviderPropertyName, identity.Provider);
        execution.SetProperty(
            TargetEndpointPropertyName,
            NormalizeSnapshotValue($"{identity.Host}:{identity.Port}", 320));
        execution.SetProperty(
            TargetDatabasePropertyName,
            NormalizeSnapshotValue(identity.Database, 128));
        execution.SetProperty(
            TargetTablePropertyName,
            NormalizeSnapshotValue(
                canonicalTargetTable,
                PipelineExecutionConsts.MaxCanonicalTargetTableLength));
        execution.SetProperty(
            TargetConfigVersionPropertyName,
            NormalizeSnapshotValue(target.ConcurrencyStamp, 40));
        execution.SetProperty(
            TargetResourceGroupPropertyName,
            NormalizeSnapshotValue(identity.TargetResourceGroup, 128));
        execution.SetProperty(
            SourceDataSourceIdPropertyName,
            source.Id.ToString("N"));
        execution.SetProperty(
            SourceDataSourceNamePropertyName,
            NormalizeSnapshotValue(source.Name, 128));
        execution.SetProperty(
            SourceConfigVersionPropertyName,
            NormalizeSnapshotValue(source.ConcurrencyStamp, 40));
        execution.SetProperty(WriteModePropertyName, identity.WriteMode);
        execution.SetProperty(
            SourceQuerySha256PropertyName,
            identity.SourceQuerySha256);
        execution.SetProperty(
            TargetIdentityVersionPropertyName,
            receiptEnabled ? "receipt-v1" : "group-v1");
    }

    private static ExecutionIdentity BuildExecutionIdentity(
        DataPipeline pipeline,
        DataSource source,
        DataSource target)
    {
        if (!source.IsEnabled || !target.IsEnabled ||
            source.Type != DataSourceType.RelationalDatabase ||
            target.Type != DataSourceType.RelationalDatabase)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid);
        }

        var transform = EtlTransformConfig.Parse(pipeline.TransformConfig);
        var legacyIdentity = ComputeTargetIdentity(
            target.Config,
            pipeline.TransformConfig);
        var activation = DataPipelineReceiptActivation.Read(pipeline);
        if (transform.ReconciliationStrategy ==
            EtlReconciliationStrategy.ManualV0)
        {
            if (DataPipelineReceiptActivation.HasAny(pipeline))
            {
                throw ReceiptInvalid();
            }

            return new ExecutionIdentity(
                legacyIdentity,
                transform,
                Activation: null,
                ExecutionContractHash: null,
                [legacyIdentity.Resource]);
        }

        if (activation == null)
        {
            throw ReceiptInvalid();
        }

        var executionContractHash = EtlReceiptHashes.ComputeExecutionContract(
            pipeline.Id,
            source.Id,
            source.ConcurrencyStamp,
            target.Id,
            target.ConcurrencyStamp,
            legacyIdentity.Provider,
            activation.ResourceInstanceId,
            activation.TargetCoordinationIdentityHash,
            legacyIdentity.Resource,
            activation.VerifiedTargetResourceHash,
            activation.CanonicalTargetTable,
            transform.Mode,
            legacyIdentity.SourceQuerySha256);
        if (!string.Equals(
                executionContractHash,
                activation.ExecutionContractHash,
                StringComparison.Ordinal))
        {
            throw ReceiptInvalid();
        }

        return new ExecutionIdentity(
            legacyIdentity,
            transform,
            activation,
            executionContractHash,
            [
                legacyIdentity.Resource,
                activation.VerifiedTargetResourceHash,
            ]);
    }

    private static BusinessException ReceiptInvalid() =>
        new(
            SwapMidDomainErrorCodes.DataIntegration
                .PipelineTargetReceiptInvalid);

    private static string NormalizeSnapshotValue(string value, int maxLength)
    {
        var normalized = new string(value
            .Trim()
            .Select(character => char.IsControl(character) ? '?' : character)
            .Take(maxLength)
            .ToArray());
        return normalized;
    }

    internal static TargetIdentity ComputeTargetIdentity(
        string targetConfig,
        string? transformConfig)
    {
        try
        {
            var target = RelationalDataSourceConnection.Parse(targetConfig);
            var transform = EtlTransformConfig.Parse(transformConfig);
            var provider = target.NormalizedProvider switch
            {
                "mssql" => "sqlserver",
                "mariadb" => "mysql",
                "postgres" or "npgsql" => "postgresql",
                var value => value,
            };
            var port = target.Port > 0
                ? target.Port
                : provider switch
                {
                    "mysql" => 3306,
                    "postgresql" => 5432,
                    _ => 1433,
                };
            var targetResourceGroup =
                target.TargetResourceGroup?.Trim() ?? string.Empty;
            if (!RelationalDataSourceConfigContract
                    .IsValidTargetResourceGroup(targetResourceGroup))
            {
                // 主机名、IP 与 DNS 别名都可能指向同一物理库。没有管理员维护的稳定标识时，
                // 事后 OutcomeUnknown 隔离无法证明两个 DataSource 不同，必须在落 Running 前失败关闭。
                throw new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineTargetResourceGroupRequired);
            }
            if (provider == "sqlserver" &&
                !transform.TargetTable.Contains('.'))
            {
                // SQL Server 未限定表名按登录用户 default_schema 解析，不一定是 dbo。
                // 在拿执行 fence 前无法仅凭配置安全归一化，故强制 schema.table，避免
                // orders 与 sales.orders 指向同表却生成两个资源键。
                throw new BusinessException(
                    SwapMidDomainErrorCodes.DataIntegration
                        .PipelineExecutionInputInvalid);
            }

            var targetTable = transform.TargetTable.Contains('.')
                ? transform.TargetTable
                : provider switch
                {
                    "mysql" => $"{target.Database}.{transform.TargetTable}",
                    "postgresql" => $"public.{transform.TargetTable}",
                    _ => transform.TargetTable,
                };
            // 仅使用目标物理身份与目标表，不能把 sourceQuery/mode 放入资源键；否则两个
            // 不同管道写同一表会取得不同 fence。凭据也不得进入锁名或诊断信息。
            var canonicalIdentity = string.Join(
                '\u001f',
                provider,
                targetResourceGroup,
                targetTable.Trim()).ToUpperInvariant();
            return new TargetIdentity(
                Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(canonicalIdentity))),
                provider,
                target.Host?.Trim() ?? string.Empty,
                port,
                target.Database?.Trim() ?? string.Empty,
                targetTable.Trim(),
                targetResourceGroup,
                transform.Mode == EtlWriteMode.Truncate
                    ? "truncate"
                    : "append",
                Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(transform.SourceQuery))));
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new BusinessException(
                SwapMidDomainErrorCodes.DataIntegration
                    .PipelineExecutionInputInvalid);
        }
    }

    private static string? ReadStringProperty(
        PipelineExecution execution,
        string propertyName)
    {
        if (!execution.ExtraProperties.TryGetValue(propertyName, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json =>
                json.GetString(),
            _ => null,
        };
    }


    internal sealed record TargetIdentity(
        string Resource,
        string Provider,
        string Host,
        int Port,
        string Database,
        string TargetTable,
        string TargetResourceGroup,
        string WriteMode,
        string SourceQuerySha256);

    private sealed record ExecutionIdentity(
        TargetIdentity LegacyIdentity,
        EtlTransformConfig Transform,
        DataPipelineReceiptActivation.Activation? Activation,
        string? ExecutionContractHash,
        IReadOnlyList<string> TargetResources)
    {
        internal string CanonicalTargetTable =>
            Activation?.CanonicalTargetTable ?? LegacyIdentity.TargetTable;

        internal EtlExecutionContext CreateContext(
            Guid runId,
            Guid pipelineId) =>
            new(
                runId,
                pipelineId,
                LegacyIdentity.Resource,
                Activation?.VerifiedTargetResourceHash,
                ExecutionContractHash,
                Activation?.TargetCoordinationIdentityHash,
                LegacyIdentity.SourceQuerySha256,
                CanonicalTargetTable,
                Transform.Mode,
                Activation == null
                    ? null
                    : new EtlTargetReceiptContext(
                        Activation.ResourceInstanceId));
    }

}
