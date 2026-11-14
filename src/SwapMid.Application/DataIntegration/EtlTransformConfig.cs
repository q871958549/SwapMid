using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>ETL 写入模式。</summary>
public enum EtlWriteMode
{
    /// <summary>追加：只 INSERT，不动目标表已有数据。</summary>
    Append,

    /// <summary>全量替换：先清空目标表（DELETE）再写入。</summary>
    Truncate,
}

/// <summary>ETL 外部结果对账协议。</summary>
public enum EtlReconciliationStrategy
{
    /// <summary>兼容旧管道：仅依赖人工目标库核验。</summary>
    ManualV0 = 0,

    /// <summary>目标 DML 与 ExecutionReceiptV1 在同一事务提交。</summary>
    TargetReceiptV1 = 1,
}

/// <summary>管道 ETL 执行结果。</summary>
public sealed record EtlRunResult(
    long Rows,
    bool TargetTruncated,
    EtlTargetReceiptEvidence? ReceiptEvidence = null);

/// <summary>
/// ETL 转换配置（<see cref="DataPipeline.TransformConfig"/> JSON 投影）：
/// <c>{ "sourceQuery": "SELECT ...", "targetTable": "schema.table", "mode": "append|truncate|append-receipt-v1|truncate-receipt-v1" }</c>。
/// receipt-v1 使用新的 mode 作为旧二进制的 fail-closed poison pill；不得改为会被旧解析器
/// 静默忽略的可选 JSON 字段。
/// </summary>
public sealed class EtlTransformConfig
{
    public string SourceQuery { get; init; } = string.Empty;
    public string TargetTable { get; init; } = string.Empty;
    public EtlWriteMode Mode { get; init; } = EtlWriteMode.Append;
    public EtlReconciliationStrategy ReconciliationStrategy { get; init; } =
        EtlReconciliationStrategy.ManualV0;

    // 合法表名：字母/下划线开头 + 字母数字下划线，可含一级 schema.table。
    // 表名不能参数化，因此只能通过白名单校验后再按数据库方言引用。
    private static readonly Regex IdentifierPattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$", RegexOptions.Compiled);

    public static EtlTransformConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw InvalidInput();
        }

        EtlTransformConfig cfg;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw InvalidInput();
            }

            string? Get(string key) =>
                root.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : null;

            string? mode = null;
            if (root.TryGetProperty("mode", out var modeElement))
            {
                if (modeElement.ValueKind != JsonValueKind.String)
                {
                    throw InvalidInput();
                }

                mode = modeElement.GetString()?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(mode))
                {
                    throw InvalidInput();
                }
            }

            cfg = new EtlTransformConfig
            {
                SourceQuery = Get("sourceQuery")?.Trim() ?? string.Empty,
                TargetTable = Get("targetTable")?.Trim() ?? string.Empty,
                Mode = mode switch
                {
                    null or "append" or "append-receipt-v1" =>
                        EtlWriteMode.Append,
                    "truncate" or "truncate-receipt-v1" =>
                        EtlWriteMode.Truncate,
                    _ => throw InvalidInput(),
                },
                ReconciliationStrategy = mode switch
                {
                    "append-receipt-v1" or "truncate-receipt-v1" =>
                        EtlReconciliationStrategy.TargetReceiptV1,
                    _ => EtlReconciliationStrategy.ManualV0,
                },
            };
        }
        catch (JsonException)
        {
            throw InvalidInput();
        }

        if (string.IsNullOrWhiteSpace(cfg.SourceQuery))
        {
            throw InvalidInput();
        }

        if (!IsValidTargetTable(cfg.TargetTable))
        {
            throw InvalidInput();
        }

        return cfg;
    }

    internal static bool IsValidTargetTable(string? targetTable) =>
        !string.IsNullOrWhiteSpace(targetTable) &&
        IdentifierPattern.IsMatch(targetTable);

    private static BusinessException InvalidInput() =>
        new(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
}
