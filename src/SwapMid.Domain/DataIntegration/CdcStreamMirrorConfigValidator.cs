using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// CDC 关系表行级镜像的字段重映射与软删除墓碑列校验辅助类。
/// </summary>
public sealed record CdcFieldMappingRule(string SourceColumn, string TargetColumn);

public static partial class CdcStreamMirrorConfigValidator
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ColumnIdentifierRegex();

    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static bool IsValidColumnIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value) && ColumnIdentifierRegex().IsMatch(value.Trim());

    public static string? NormalizeIdentifier(string? value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = Check.NotNullOrWhiteSpace(value, paramName, maxLength).Trim();
        if (!ColumnIdentifierRegex().IsMatch(trimmed))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"列名 \"{trimmed}\" 格式不合法，仅允许字母、数字与下划线且不能以数字开头。");
        }

        return trimmed;
    }

    public static IReadOnlyList<CdcFieldMappingRule> ParseFieldMappings(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Array.Empty<CdcFieldMappingRule>();
        }

        List<CdcFieldMappingRuleInput>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<CdcFieldMappingRuleInput>>(rawJson, CamelJson);
        }
        catch (JsonException)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "CDC 字段映射（TargetFieldMappingsJson）必须是有效的 JSON 数组。");
        }

        if (parsed == null || parsed.Count == 0)
        {
            return Array.Empty<CdcFieldMappingRule>();
        }

        if (parsed.Count > CdcStreamConsts.MaxTargetFieldMappingCount)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"CDC 字段映射数量不能超过 {CdcStreamConsts.MaxTargetFieldMappingCount} 项。");
        }

        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rules = new List<CdcFieldMappingRule>(parsed.Count);
        foreach (var item in parsed)
        {
            var src = item?.SourceColumn?.Trim() ?? string.Empty;
            var tgt = item?.TargetColumn?.Trim() ?? string.Empty;
            if (!IsValidColumnIdentifier(src) || !IsValidColumnIdentifier(tgt))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "CDC 字段映射中的源列名与目标列名均须为合法的 SQL 标识符。");
            }

            if (!seenTargets.Add(tgt))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"CDC 字段映射存在重复的目标列 \"{tgt}\"（忽略大小写）。");
            }

            rules.Add(new CdcFieldMappingRule(src, tgt));
        }

        return rules;
    }

    public static string? NormalizeFieldMappingsJson(string? rawJson)
    {
        var rules = ParseFieldMappings(rawJson);
        if (rules.Count == 0)
        {
            return null;
        }

        var normalized = JsonSerializer.Serialize(rules, CamelJson);
        if (normalized.Length > CdcStreamConsts.MaxTargetFieldMappingsJsonLength)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"CDC 字段映射序列化长度超出 {CdcStreamConsts.MaxTargetFieldMappingsJsonLength} 字符上限。");
        }

        return normalized;
    }

    public static void ValidateMirrorColumns(
        CdcTargetMode targetMode,
        string? targetSinkKeyColumns,
        string? fieldMappingsJson,
        CdcDeletePolicy deletePolicy,
        string? softDeleteColumn,
        string? softDeleteTimestampColumn)
    {
        if (targetMode != CdcTargetMode.RelationalTable)
        {
            return;
        }

        var keyColumns = string.IsNullOrWhiteSpace(targetSinkKeyColumns)
            ? Array.Empty<string>()
            : targetSinkKeyColumns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fieldMappings = ParseFieldMappings(fieldMappingsJson);

        var mappedTargetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in fieldMappings)
        {
            mappedTargetSet.Add(rule.TargetColumn);
        }

        if (mappedTargetSet.Count > 0)
        {
            foreach (var key in keyColumns)
            {
                if (!mappedTargetSet.Contains(key))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"配置显式字段映射时，业务主键列 \"{key}\" 必须包含在映射目标列中。");
                }
            }
        }

        if (deletePolicy == CdcDeletePolicy.SoftDelete)
        {
            if (string.IsNullOrWhiteSpace(softDeleteColumn))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "启用软删除（SoftDelete）策略时必须填写软删除标记列名（SoftDeleteColumn）。");
            }

            var keySet = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
            if (keySet.Contains(softDeleteColumn))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"软删除标记列 \"{softDeleteColumn}\" 不能与业务主键列同名。");
            }

            if (mappedTargetSet.Contains(softDeleteColumn))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"软删除标记列 \"{softDeleteColumn}\" 由系统托管维护，不能同时在显式字段映射中覆盖。");
            }

            if (!string.IsNullOrWhiteSpace(softDeleteTimestampColumn))
            {
                if (string.Equals(softDeleteColumn, softDeleteTimestampColumn, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", "软删除标记列与软删除时间戳列不能使用相同列名。");
                }

                if (keySet.Contains(softDeleteTimestampColumn) || mappedTargetSet.Contains(softDeleteTimestampColumn))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"软删除时间戳列 \"{softDeleteTimestampColumn}\" 不能与业务主键或显式字段映射目标列同名。");
                }
            }
        }
    }

    public static int? NormalizeLakehouseCompactMinFiles(CdcTargetMode targetMode, int? value)
    {
        if (targetMode != CdcTargetMode.ExternalSink || !value.HasValue || value.Value == 0)
        {
            return null;
        }

        if (value.Value < CdcStreamConsts.MinLakehouseCompactMinFiles ||
            value.Value > CdcStreamConsts.MaxLakehouseCompactMinFiles)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData(
                    "Reason",
                    $"Lakehouse 自动压缩分片阈值（LakehouseCompactMinFiles）须在 {CdcStreamConsts.MinLakehouseCompactMinFiles}~{CdcStreamConsts.MaxLakehouseCompactMinFiles} 之间。");
        }

        return value.Value;
    }

    private sealed class CdcFieldMappingRuleInput
    {
        public string? SourceColumn { get; set; }
        public string? TargetColumn { get; set; }
    }
}
