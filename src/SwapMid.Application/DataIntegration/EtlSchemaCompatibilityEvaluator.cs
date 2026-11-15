using System;
using System.Collections.Generic;
using System.Linq;

namespace SwapMid.DataIntegration;

/// <summary>
/// 源查询列、显式字段映射与目标表结构的静态兼容性校验器。
/// </summary>
internal static class EtlSchemaCompatibilityEvaluator
{
    internal static void Evaluate(
        EtlTransformConfig cfg,
        PipelineSchemaPreflightResultDto result)
    {
        if (result.TargetColumns.Count == 0)
        {
            result.Issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:TargetTableNotFound",
                Message = $"目标库中未找到表「{cfg.TargetTable}」或当前账号无元数据读取权限。",
            });
            return;
        }

        var sourceMap = result.SourceColumns
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var targetMap = result.TargetColumns
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var hasExplicitMappings = cfg.ColumnMappings.Count > 0;
        var mappedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (hasExplicitMappings)
        {
            EvaluateExplicitMappings(cfg, sourceMap, targetMap, mappedTargets, result.Issues);
        }
        else
        {
            EvaluateImplicitMappings(cfg.TargetTable, result.SourceColumns, targetMap, mappedTargets, result.Issues);
        }

        ValidateRequiredTargets(result.TargetColumns, mappedTargets, result.Issues);

        if (cfg.Mode == EtlWriteMode.Upsert)
        {
            ValidateUpsertConfiguration(cfg, targetMap, mappedTargets, result.Issues);
        }
    }

    private static void EvaluateExplicitMappings(
        EtlTransformConfig cfg,
        Dictionary<string, PreflightColumnDto> sourceMap,
        Dictionary<string, PreflightColumnDto> targetMap,
        HashSet<string> mappedTargets,
        List<PreflightSchemaIssueDto> issues)
    {
        foreach (var mapping in cfg.ColumnMappings)
        {
            var hasSource = sourceMap.TryGetValue(mapping.SourceColumn, out var srcCol);
            var hasTarget = targetMap.TryGetValue(mapping.TargetColumn, out var tgtCol);

            if (!hasSource)
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:MappedSourceMissing",
                    Message = $"字段映射中的源列「{mapping.SourceColumn}」未包含在源查询投影中。",
                });
            }

            if (!hasTarget)
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:MappedTargetMissing",
                    Message = $"字段映射中的目标列「{mapping.TargetColumn}」在目标表「{cfg.TargetTable}」中不存在。",
                });
            }

            if (!hasSource || !hasTarget)
            {
                continue;
            }

            mappedTargets.Add(tgtCol!.Name);
            CheckColumnPairWarnings(srcCol!, tgtCol, $"{srcCol!.Name} -> {tgtCol.Name}", issues);
        }
    }

    private static void EvaluateImplicitMappings(
        string targetTable,
        List<PreflightColumnDto> sourceColumns,
        Dictionary<string, PreflightColumnDto> targetMap,
        HashSet<string> mappedTargets,
        List<PreflightSchemaIssueDto> issues)
    {
        foreach (var srcCol in sourceColumns)
        {
            if (!targetMap.TryGetValue(srcCol.Name, out var tgtCol))
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:UnmappedSourceColumn",
                    Message = $"源查询列「{srcCol.Name}」在目标表「{targetTable}」中不存在同名列。",
                });
                continue;
            }

            mappedTargets.Add(tgtCol.Name);
            CheckColumnPairWarnings(srcCol, tgtCol, srcCol.Name, issues);
        }
    }

    private static void CheckColumnPairWarnings(
        PreflightColumnDto srcCol,
        PreflightColumnDto tgtCol,
        string label,
        List<PreflightSchemaIssueDto> issues)
    {
        if (srcCol.IsNullable && !tgtCol.IsNullable)
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "warning",
                Code = "SwapMid:ETL:NullabilityNarrowing",
                Message = $"列映射「{label}」源列允许 NULL 而目标列为 NOT NULL，若源数据含空值将在暂存校验阶段失败。",
            });
        }

        if (IsIncompatibleTypeFamily(srcCol.DataType, tgtCol.DataType))
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "warning",
                Code = "SwapMid:ETL:TypeFamilyMismatch",
                Message = $"列映射「{label}」源类型（{srcCol.DataType}）与目标类型（{tgtCol.DataType}）跨类型族，可能导致转换失败或精度损失。",
            });
        }
    }

    private static void ValidateRequiredTargets(
        List<PreflightColumnDto> targetColumns,
        HashSet<string> mappedTargets,
        List<PreflightSchemaIssueDto> issues)
    {
        foreach (var tgtCol in targetColumns)
        {
            if (!mappedTargets.Contains(tgtCol.Name) && !tgtCol.IsNullable && !tgtCol.HasDefaultOrIdentity)
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:MissingRequiredTargetColumn",
                    Message = $"目标表必填列「{tgtCol.Name}」（NOT NULL 且无默认值）未在源查询或字段映射中提供。",
                });
            }
        }
    }

    private static void ValidateUpsertConfiguration(
        EtlTransformConfig cfg,
        Dictionary<string, PreflightColumnDto> targetMap,
        HashSet<string> mappedTargets,
        List<PreflightSchemaIssueDto> issues)
    {
        foreach (var key in cfg.KeyColumns)
        {
            if (!mappedTargets.Contains(key))
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:UpsertKeyMissingInSource",
                    Message = $"Upsert 业务主键列「{key}」未包含在源查询或目标字段映射中。",
                });
            }

            if (!targetMap.TryGetValue(key, out var targetKeyCol))
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:UpsertKeyMissingInTarget",
                    Message = $"Upsert 业务主键列「{key}」在目标表中不存在。",
                });
                continue;
            }

            if (targetKeyCol.IsNullable)
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:UpsertKeyNullable",
                    Message = $"Upsert 目标主键列「{key}」允许 NULL，必须声明为 NOT NULL。",
                });
            }

            if (!targetKeyCol.IsUniqueIndexed)
            {
                issues.Add(new PreflightSchemaIssueDto
                {
                    Severity = "error",
                    Code = "SwapMid:ETL:UpsertKeyNotUniqueIndexed",
                    Message = $"Upsert 目标主键列「{key}」缺少非过滤唯一索引或主键约束。",
                });
            }
        }

        var keySet = cfg.KeyColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (mappedTargets.Count(target => !keySet.Contains(target)) == 0)
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:UpsertNoUpdateColumns",
                Message = "Upsert 除业务主键外至少需要包含 1 个待更新目标业务列。",
            });
        }

        if (!string.IsNullOrWhiteSpace(cfg.WatermarkColumn) &&
            (!mappedTargets.Contains(cfg.WatermarkColumn) || !targetMap.ContainsKey(cfg.WatermarkColumn)))
        {
            issues.Add(new PreflightSchemaIssueDto
            {
                Severity = "error",
                Code = "SwapMid:ETL:WatermarkColumnMissing",
                Message = $"Upsert 水位线列「{cfg.WatermarkColumn}」必须同时存在于写入映射与目标表中。",
            });
        }
    }

    private static bool IsIncompatibleTypeFamily(string sourceType, string targetType)
    {
        static string Classify(string raw)
        {
            var t = raw.Trim().ToLowerInvariant();
            if (t.Contains("int") || t.Contains("numeric") || t.Contains("decimal") ||
                t.Contains("float") || t.Contains("double") || t.Contains("real") || t.Contains("money"))
            {
                return "number";
            }

            if (t.Contains("bool") || t == "bit") return "boolean";
            if (t.Contains("uuid") || t.Contains("uniqueidentifier")) return "uuid";
            if (t.Contains("date") || t.Contains("time")) return "temporal";
            if (t.Contains("char") || t.Contains("text") || t.Contains("json") || t.Contains("xml")) return "text";
            return "other";
        }

        var sf = Classify(sourceType);
        var tf = Classify(targetType);
        if (sf == "other" || tf == "other" || sf == tf) return false;
        return tf != "text";
    }
}
