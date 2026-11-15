using System;
using System.Collections.Generic;
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

    /// <summary>幂等合并：按业务唯一键执行冲突更新与新行插入（Upsert）。</summary>
    Upsert,
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

/// <summary>Upsert 唯一键与可选水位线规格。</summary>
internal sealed record EtlUpsertSpec(
    IReadOnlyList<string> KeyColumns,
    string? WatermarkColumn);

/// <summary>源列到目标列的显式字段映射。</summary>
public sealed record EtlColumnMapping(string SourceColumn, string TargetColumn);

/// <summary>
/// ETL 转换配置（<see cref="DataPipeline.TransformConfig"/> JSON 投影）：
/// <c>{ "sourceQuery": "SELECT ...", "targetTable": "schema.table", "mode": "append|truncate|upsert|append-receipt-v1|truncate-receipt-v1|upsert-receipt-v1", "keyColumns": ["id"], "watermarkColumn": "updated_at", "columnMappings": [{ "sourceColumn": "src", "targetColumn": "tgt" }] }</c>。
/// receipt-v1 与 upsert 使用显式 mode 作为旧二进制的 fail-closed poison pill；不得改为会被旧解析器
/// 静默忽略的可选 JSON 字段。
/// </summary>
public sealed class EtlTransformConfig
{
    public const int MaxUpsertKeyColumns = 16;
    public const int MaxColumnMappings = 256;

    public string SourceQuery { get; init; } = string.Empty;
    public string TargetTable { get; init; } = string.Empty;
    public EtlWriteMode Mode { get; init; } = EtlWriteMode.Append;
    public EtlReconciliationStrategy ReconciliationStrategy { get; init; } =
        EtlReconciliationStrategy.ManualV0;
    public IReadOnlyList<string> KeyColumns { get; init; } = Array.Empty<string>();
    public string? WatermarkColumn { get; init; }
    public IReadOnlyList<EtlColumnMapping> ColumnMappings { get; init; } = Array.Empty<EtlColumnMapping>();

    internal EtlUpsertSpec? UpsertSpec =>
        Mode == EtlWriteMode.Upsert
            ? new EtlUpsertSpec(KeyColumns, WatermarkColumn)
            : null;

    // 合法表名：字母/下划线开头 + 字母数字下划线，可含一级 schema.table。
    // 表名不能参数化，因此只能通过白名单校验后再按数据库方言引用。
    private static readonly Regex IdentifierPattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$", RegexOptions.Compiled);

    private static readonly Regex ColumnIdentifierPattern =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

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

            var writeMode = mode switch
            {
                null or "append" or "append-receipt-v1" =>
                    EtlWriteMode.Append,
                "truncate" or "truncate-receipt-v1" =>
                    EtlWriteMode.Truncate,
                "upsert" or "upsert-receipt-v1" =>
                    EtlWriteMode.Upsert,
                _ => throw InvalidInput(),
            };

            var keyColumns = ParseKeyColumns(root, writeMode);
            var watermarkColumn = ParseWatermarkColumn(root, writeMode, keyColumns);
            var columnMappings = ParseColumnMappings(root);

            cfg = new EtlTransformConfig
            {
                SourceQuery = Get("sourceQuery")?.Trim() ?? string.Empty,
                TargetTable = Get("targetTable")?.Trim() ?? string.Empty,
                Mode = writeMode,
                ReconciliationStrategy = mode switch
                {
                    "append-receipt-v1" or "truncate-receipt-v1" or "upsert-receipt-v1" =>
                        EtlReconciliationStrategy.TargetReceiptV1,
                    _ => EtlReconciliationStrategy.ManualV0,
                },
                KeyColumns = keyColumns,
                WatermarkColumn = watermarkColumn,
                ColumnMappings = columnMappings,
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

    internal static bool IsValidColumnName(string? columnName) =>
        !string.IsNullOrWhiteSpace(columnName) &&
        ColumnIdentifierPattern.IsMatch(columnName);

    private static IReadOnlyList<string> ParseKeyColumns(
        JsonElement root,
        EtlWriteMode writeMode)
    {
        if (!root.TryGetProperty("keyColumns", out var keyElement) ||
            keyElement.ValueKind == JsonValueKind.Null)
        {
            if (writeMode == EtlWriteMode.Upsert)
            {
                throw InvalidInput();
            }

            return Array.Empty<string>();
        }

        if (keyElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidInput();
        }

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in keyElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw InvalidInput();
            }

            var trimmed = item.GetString()?.Trim();
            if (!IsValidColumnName(trimmed) || !seen.Add(trimmed!))
            {
                throw InvalidInput();
            }

            keys.Add(trimmed!);
        }

        if (writeMode == EtlWriteMode.Upsert)
        {
            if (keys.Count == 0 || keys.Count > MaxUpsertKeyColumns)
            {
                throw InvalidInput();
            }

            return keys;
        }

        if (keys.Count > 0)
        {
            throw InvalidInput();
        }

        return Array.Empty<string>();
    }

    private static string? ParseWatermarkColumn(
        JsonElement root,
        EtlWriteMode writeMode,
        IReadOnlyList<string> keyColumns)
    {
        if (!root.TryGetProperty("watermarkColumn", out var watermarkElement) ||
            watermarkElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (watermarkElement.ValueKind != JsonValueKind.String)
        {
            throw InvalidInput();
        }

        var trimmed = watermarkElement.GetString()?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (writeMode != EtlWriteMode.Upsert || !IsValidColumnName(trimmed))
        {
            throw InvalidInput();
        }

        foreach (var key in keyColumns)
        {
            if (string.Equals(key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidInput();
            }
        }

        return trimmed;
    }

    private static IReadOnlyList<EtlColumnMapping> ParseColumnMappings(JsonElement root)
    {
        if (!root.TryGetProperty("columnMappings", out var mappingsElement) ||
            mappingsElement.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<EtlColumnMapping>();
        }

        if (mappingsElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidInput();
        }

        var mappings = new List<EtlColumnMapping>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in mappingsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("sourceColumn", out var sourceElement) ||
                sourceElement.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("targetColumn", out var targetElement) ||
                targetElement.ValueKind != JsonValueKind.String)
            {
                throw InvalidInput();
            }

            var sourceColumn = sourceElement.GetString()?.Trim();
            var targetColumn = targetElement.GetString()?.Trim();
            if (!IsValidColumnName(sourceColumn) ||
                !IsValidColumnName(targetColumn) ||
                !seenTargets.Add(targetColumn!))
            {
                throw InvalidInput();
            }

            mappings.Add(new EtlColumnMapping(sourceColumn!, targetColumn!));
            if (mappings.Count > MaxColumnMappings)
            {
                throw InvalidInput();
            }
        }

        return mappings;
    }

    private static BusinessException InvalidInput() =>
        new(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
}
