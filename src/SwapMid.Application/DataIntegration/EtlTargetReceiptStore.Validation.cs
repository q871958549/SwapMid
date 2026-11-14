using System;

namespace SwapMid.DataIntegration;

public sealed partial class EtlTargetReceiptStore
{
    internal static byte[] ToRfc4122Bytes(Guid value) =>
        value.ToByteArray(bigEndian: true);

    internal static Guid FromRfc4122Bytes(byte[] value)
    {
        if (value.Length != 16)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return new Guid(value, bigEndian: true);
    }

    internal static string NormalizeProvider(string provider) =>
        provider switch
        {
            "mssql" => "sqlserver",
            "mariadb" => "mysql",
            "postgres" or "npgsql" => "postgresql",
            _ => provider,
        };

    internal static void EnsureBusinessTargetIsNotReserved(
        string canonicalTargetTable,
        RelationalDataSourceConnection.Probe probe,
        string? currentDatabase)
    {
        var provider = NormalizeProvider(probe.NormalizedProvider);
        var reserved = provider switch
        {
            "sqlserver" => new[] { SqlServerControlTable, SqlServerReceiptTable },
            "postgresql" => new[] { PostgreSqlControlTable, PostgreSqlReceiptTable },
            _ => new[]
            {
                $"{currentDatabase}.{MySqlControlTable}",
                $"{currentDatabase}.{MySqlReceiptTable}",
            },
        };
        // 证据对象名称作为保留命名空间，比普通 MySQL 业务表更严格：
        // 即使 lower_case_table_names=0 允许大小写异名并存，也拒绝与固定证据表
        // 仅大小写不同的业务目标。这一保守规则同时覆盖 1/2 模式的折叠语义，
        // 避免配置 database 与 INFORMATION_SCHEMA 规范名大小写不同时漏过。
        var comparison = StringComparison.OrdinalIgnoreCase;
        foreach (var name in reserved)
        {
            if (string.Equals(canonicalTargetTable, name, comparison))
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }
    }

    internal static bool ReceiptEquals(
        EtlTargetReceiptEvidence left,
        EtlTargetReceiptEvidence right) =>
        left.RunId == right.RunId &&
        left.PipelineId == right.PipelineId &&
        left.ResourceInstanceId == right.ResourceInstanceId &&
        string.Equals(
            left.LegacyTargetResourceHash,
            right.LegacyTargetResourceHash,
            StringComparison.Ordinal) &&
        string.Equals(
            left.VerifiedTargetResourceHash,
            right.VerifiedTargetResourceHash,
            StringComparison.Ordinal) &&
        string.Equals(
            left.ExecutionContractHash,
            right.ExecutionContractHash,
            StringComparison.Ordinal) &&
        string.Equals(
            left.SourceQueryHash,
            right.SourceQueryHash,
            StringComparison.Ordinal) &&
        string.Equals(
            left.TargetSchemaHash,
            right.TargetSchemaHash,
            StringComparison.Ordinal) &&
        string.Equals(
            left.BatchDigest,
            right.BatchDigest,
            StringComparison.Ordinal) &&
        left.WriteMode == right.WriteMode &&
        left.ProcessedRows == right.ProcessedRows;

    private static RelationalDataSourceConnection.Probe ParseSupportedTarget(
        string config)
    {
        RelationalDataSourceConnection.Probe probe;
        try
        {
            probe = RelationalDataSourceConnection.Parse(config);
        }
        catch
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        // v1 开放 SQL Server 与 PostgreSQL（二者系统目录对触发器/约束均全局可读且无假空集）。
        // MySQL/MariaDB 的 TRIGGERS catalog 在运行账号缺少 TRIGGER 权限时会返回假空，
        // 故在独立 inspector 协议落地前继续失败关闭。
        if (probe.NormalizedProvider is not (
            "sqlserver" or "mssql" or
            "postgresql" or "postgres" or "npgsql"))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        EnsureAuthenticatedReceiptTransport(probe);

        return probe;
    }

    private static void EnsureAuthenticatedReceiptTransport(
        RelationalDataSourceConnection.Probe probe)
    {
        if (probe.NormalizedProvider is
            "sqlserver" or "mssql" or
            "postgresql" or "postgres" or "npgsql")
        {
            if (!probe.Encrypt || probe.TrustServerCertificate)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            return;
        }

        throw EtlPipelineRunner.InvalidInput();
    }

    private static void EnsureReceiptModeSupported(
        RelationalDataSourceConnection.Probe probe,
        EtlTransformConfig transform)
    {
        if (transform.ReconciliationStrategy !=
            EtlReconciliationStrategy.TargetReceiptV1)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        EnsureReceiptWriteModeSupported(probe, transform.Mode);
    }

    private static void EnsureReceiptWriteModeSupported(
        RelationalDataSourceConnection.Probe probe,
        EtlWriteMode writeMode)
    {
        if (writeMode is not (EtlWriteMode.Append or EtlWriteMode.Truncate) ||
            (probe.NormalizedProvider is "mysql" or "mariadb" &&
             writeMode == EtlWriteMode.Truncate))
        {
            throw EtlPipelineRunner.InvalidInput();
        }
    }
}
