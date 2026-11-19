using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SwapMid.DataIntegration;

namespace SwapMid.Mcp;

/// <summary>
/// AI 辅助只读字段映射与管道配置建议处理器（<c>propose_pipeline_column_mappings</c>）：
/// 基于确定性零行 Schema 预检结果自动匹配精确/归一化列、推导候选主键/水位线列并生成 Draft 可用 transformConfig。
/// </summary>
internal static class McpPipelineCopilotHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly string[] WatermarkCandidateTokens =
    [
        "updatedat",
        "lastmodificationtime",
        "modifiedat",
        "updatetime",
        "lastupdated",
        "modifytime",
    ];

    internal static async Task<object> ExecuteAsync(
        IServiceProvider serviceProvider,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        var preflightService = serviceProvider.GetRequiredService<IDataPipelineSchemaPreflightAppService>();
        var sourceId = McpJsonRpcHelpers.ReadRequiredGuid(args, "sourceId");
        var targetId = McpJsonRpcHelpers.ReadOptionalGuid(args, "targetId");
        var sourceQuery = McpJsonRpcHelpers.ReadRequiredString(args, "sourceQuery");
        var targetTable = McpJsonRpcHelpers.ReadRequiredString(args, "targetTable");
        var requestedMode = NormalizeMode(McpJsonRpcHelpers.ReadOptionalString(args, "mode"));

        var probeConfig = JsonSerializer.Serialize(
            new
            {
                sourceQuery,
                targetTable,
                mode = "append",
            },
            JsonOptions);

        var preflight = await preflightService.PreflightSchemaAsync(
            new PreflightPipelineSchemaInput
            {
                SourceId = sourceId,
                TargetId = targetId,
                TransformConfig = probeConfig,
            },
            cancellationToken);

        var mappings = BuildDeterministicMappings(preflight.SourceColumns, preflight.TargetColumns);
        var mappedTargets = new HashSet<string>(
            mappings.Select(m => m.TargetColumn),
            StringComparer.OrdinalIgnoreCase);

        var unmappedRequiredTargets = preflight.TargetColumns
            .Where(c => !c.IsNullable && !mappedTargets.Contains(c.Name))
            .Select(c => new { c.Name, c.DataType })
            .ToList();

        var unmappedSourceColumns = preflight.SourceColumns
            .Where(c => !mappings.Any(m => string.Equals(m.SourceColumn, c.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(c => new { c.Name, c.DataType })
            .ToList();

        var proposedKeyColumns = SuggestKeyColumns(mappings);
        var proposedWatermarkColumn = SuggestWatermarkColumn(mappings);

        var effectiveMode = requestedMode == "upsert" && proposedKeyColumns.Count == 0
            ? "append"
            : requestedMode;

        var proposedTransformObj = new Dictionary<string, object?>
        {
            ["sourceQuery"] = sourceQuery,
            ["targetTable"] = targetTable,
            ["mode"] = effectiveMode,
            ["columnMappings"] = mappings.Select(m => new
            {
                sourceColumn = m.SourceColumn,
                targetColumn = m.TargetColumn,
            }).ToArray(),
        };
        if (effectiveMode == "upsert" && proposedKeyColumns.Count > 0)
        {
            proposedTransformObj["keyColumns"] = proposedKeyColumns;
            if (!string.IsNullOrWhiteSpace(proposedWatermarkColumn))
            {
                proposedTransformObj["watermarkColumn"] = proposedWatermarkColumn;
            }
        }

        var proposedTransformConfig = JsonSerializer.Serialize(proposedTransformObj, JsonOptions);

        return new
        {
            sourceProvider = preflight.SourceProvider,
            targetProvider = preflight.TargetProvider,
            sourceColumnCount = preflight.SourceColumns.Count,
            targetColumnCount = preflight.TargetColumns.Count,
            proposedMappings = mappings,
            unmappedRequiredTargets,
            unmappedSourceColumns,
            proposedMode = effectiveMode,
            proposedKeyColumns,
            proposedWatermarkColumn,
            readyForDraft = preflight.SourceColumns.Count > 0 && unmappedRequiredTargets.Count == 0,
            proposedTransformConfig,
            preflightIssues = preflight.Issues,
            nextStepHint = "Pass proposedTransformConfig into draft_data_pipeline to create a Draft pipeline, then verify via preflight_pipeline_schema before human activation.",
        };
    }

    private static List<ProposedMappingItem> BuildDeterministicMappings(
        List<PreflightColumnDto> sourceColumns,
        List<PreflightColumnDto> targetColumns)
    {
        var result = new List<ProposedMappingItem>();
        if (sourceColumns.Count == 0)
        {
            return result;
        }

        if (targetColumns.Count == 0)
        {
            foreach (var src in sourceColumns)
            {
                result.Add(new ProposedMappingItem(
                    src.Name,
                    src.Name,
                    src.DataType,
                    null,
                    "schema_on_write",
                    TypeCompatible: true));
            }

            return result;
        }

        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactTargetMap = targetColumns
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var normTargetMap = targetColumns
            .GroupBy(t => NormalizeToken(t.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Pass 1: exact case-insensitive matches
        foreach (var src in sourceColumns)
        {
            if (exactTargetMap.TryGetValue(src.Name, out var tgt) && usedTargets.Add(tgt.Name))
            {
                result.Add(new ProposedMappingItem(
                    src.Name,
                    tgt.Name,
                    src.DataType,
                    tgt.DataType,
                    "exact",
                    AreTypeFamiliesCompatible(src.DataType, tgt.DataType)));
            }
        }

        var mappedSources = new HashSet<string>(result.Select(r => r.SourceColumn), StringComparer.OrdinalIgnoreCase);

        // Pass 2: normalized token matches (snake_case <-> camelCase / PascalCase)
        foreach (var src in sourceColumns)
        {
            if (mappedSources.Contains(src.Name)) continue;
            var token = NormalizeToken(src.Name);
            if (!string.IsNullOrEmpty(token) &&
                normTargetMap.TryGetValue(token, out var tgt) &&
                usedTargets.Add(tgt.Name))
            {
                result.Add(new ProposedMappingItem(
                    src.Name,
                    tgt.Name,
                    src.DataType,
                    tgt.DataType,
                    "normalized",
                    AreTypeFamiliesCompatible(src.DataType, tgt.DataType)));
            }
        }

        return result;
    }

    private static List<string> SuggestKeyColumns(List<ProposedMappingItem> mappings)
    {
        var exactId = mappings.FirstOrDefault(m => string.Equals(m.TargetColumn, "id", StringComparison.OrdinalIgnoreCase));
        if (exactId != null)
        {
            return [exactId.TargetColumn];
        }

        var keyCandidate = mappings.FirstOrDefault(m =>
        {
            var lower = m.TargetColumn.ToLowerInvariant();
            return lower.EndsWith("_id", StringComparison.Ordinal) ||
                   lower.EndsWith("_no", StringComparison.Ordinal) ||
                   lower.EndsWith("_code", StringComparison.Ordinal) ||
                   lower.EndsWith("id", StringComparison.Ordinal);
        });

        return keyCandidate != null ? [keyCandidate.TargetColumn] : new List<string>();
    }

    private static string? SuggestWatermarkColumn(List<ProposedMappingItem> mappings)
    {
        foreach (var mapping in mappings)
        {
            var token = NormalizeToken(mapping.TargetColumn);
            if (WatermarkCandidateTokens.Contains(token, StringComparer.Ordinal))
            {
                return mapping.TargetColumn;
            }
        }

        return null;
    }

    private static string NormalizeMode(string? raw)
        => raw?.Trim().ToLowerInvariant() switch
        {
            "append" => "append",
            "truncate" => "truncate",
            _ => "upsert",
        };

    private static string NormalizeToken(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static bool AreTypeFamiliesCompatible(string? sourceType, string? targetType)
    {
        var sf = ClassifyTypeFamily(sourceType);
        var tf = ClassifyTypeFamily(targetType);
        if (sf == "other" || tf == "other" || tf == "string")
        {
            return true;
        }

        return string.Equals(sf, tf, StringComparison.Ordinal);
    }

    private static string ClassifyTypeFamily(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return "other";
        var lower = dataType.ToLowerInvariant();
        if (lower.Contains("char") || lower.Contains("text") || lower.Contains("string") || lower.Contains("json") || lower.Contains("xml")) return "string";
        if (lower.Contains("bool") || lower == "bit") return "boolean";
        if (lower.Contains("date") || lower.Contains("time")) return "datetime";
        if (lower.Contains("uuid") || lower.Contains("uniqueidentifier") || lower.Contains("guid")) return "guid";
        if (lower.Contains("int") || lower.Contains("dec") || lower.Contains("num") || lower.Contains("float") || lower.Contains("double") || lower.Contains("real") || lower.Contains("money")) return "numeric";
        if (lower.Contains("bytea") || lower.Contains("binary") || lower.Contains("blob")) return "binary";
        return "other";
    }

    private sealed record ProposedMappingItem(
        string SourceColumn,
        string TargetColumn,
        string? SourceType,
        string? TargetType,
        string MatchStrategy,
        bool TypeCompatible);
}
