using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwapMid.DataIntegration;

internal sealed record CdcCapturedTableColumn(
    string Name,
    string DataType,
    bool IsNullable);

internal sealed record CdcCapturedTableSchema(
    string Schema,
    string Table,
    IReadOnlyList<CdcCapturedTableColumn> Columns)
{
    public string FullName => $"{Schema}.{Table}";
}

internal sealed record CdcSchemaDriftEvaluation(
    bool HasDrift,
    string Summary,
    string CurrentHash,
    string CurrentJson);

/// <summary>
/// PostgreSQL CDC 上游监控表 Schema 基线指纹计算与 DDL 漂移对比器（纯函数）。
/// </summary>
internal static class CdcSchemaDriftEvaluator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    internal static (string Hash, string Json) SerializeBaseline(
        IReadOnlyList<CdcCapturedTableSchema> tables)
    {
        var ordered = tables
            .OrderBy(t => t.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Table, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Schema, StringComparer.Ordinal)
            .ThenBy(t => t.Table, StringComparer.Ordinal)
            .Select(t => new CdcCapturedTableSchema(
                t.Schema,
                t.Table,
                t.Columns.ToArray()))
            .ToArray();

        var json = JsonSerializer.Serialize(ordered, SerializerOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return (hash, json);
    }

    internal static CdcSchemaDriftEvaluation Compare(
        string? baselineJson,
        IReadOnlyList<CdcCapturedTableSchema> currentTables)
    {
        var (currentHash, currentJson) = SerializeBaseline(currentTables);
        if (string.IsNullOrWhiteSpace(baselineJson))
        {
            return new CdcSchemaDriftEvaluation(false, string.Empty, currentHash, currentJson);
        }

        CdcCapturedTableSchema[]? baselineTables;
        try
        {
            baselineTables = JsonSerializer.Deserialize<CdcCapturedTableSchema[]>(baselineJson, SerializerOptions);
        }
        catch (JsonException)
        {
            return new CdcSchemaDriftEvaluation(
                HasDrift: true,
                Summary: "已存储的 Schema 基线无法解析，请重新确认当前上游表结构。",
                CurrentHash: currentHash,
                CurrentJson: currentJson);
        }

        if (baselineTables == null)
        {
            return new CdcSchemaDriftEvaluation(false, string.Empty, currentHash, currentJson);
        }

        var (baselineHash, _) = SerializeBaseline(baselineTables);
        if (string.Equals(baselineHash, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            return new CdcSchemaDriftEvaluation(false, string.Empty, currentHash, currentJson);
        }

        var currentByTable = currentTables.ToDictionary(
            t => t.FullName,
            t => t,
            StringComparer.OrdinalIgnoreCase);
        var baselineByTable = baselineTables.ToDictionary(
            t => t.FullName,
            t => t,
            StringComparer.OrdinalIgnoreCase);

        var tableNotes = new List<string>();
        foreach (var prev in baselineTables.OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentByTable.TryGetValue(prev.FullName, out var next))
            {
                tableNotes.Add($"{prev.FullName}: 表已被删除或不在监听范围内");
                continue;
            }

            var diffParts = DescribeTableDiff(prev.Columns, next.Columns);
            if (diffParts.Count > 0)
            {
                tableNotes.Add($"{prev.FullName}: {string.Join(", ", diffParts)}");
            }
        }

        foreach (var next in currentTables.OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (!baselineByTable.ContainsKey(next.FullName))
            {
                tableNotes.Add($"{next.FullName}: +table ({next.Columns.Count} cols)");
            }
        }

        if (tableNotes.Count == 0)
        {
            return new CdcSchemaDriftEvaluation(false, string.Empty, currentHash, currentJson);
        }

        var rawSummary = string.Join("; ", tableNotes);
        var bounded = rawSummary.Length <= CdcStreamConsts.MaxSchemaDriftSummaryLength
            ? rawSummary
            : rawSummary[..(CdcStreamConsts.MaxSchemaDriftSummaryLength - 3)] + "...";
        return new CdcSchemaDriftEvaluation(true, bounded, currentHash, currentJson);
    }

    private static List<string> DescribeTableDiff(
        IReadOnlyList<CdcCapturedTableColumn> baseline,
        IReadOnlyList<CdcCapturedTableColumn> current)
    {
        var notes = new List<string>();
        var baseMap = baseline.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var currMap = current.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var col in baseline)
        {
            if (!currMap.ContainsKey(col.Name))
            {
                notes.Add($"-col {col.Name}");
            }
        }

        foreach (var col in current)
        {
            if (!baseMap.TryGetValue(col.Name, out var prev))
            {
                notes.Add($"+col {col.Name} ({col.DataType})");
                continue;
            }

            var typeChanged = !string.Equals(prev.DataType, col.DataType, StringComparison.OrdinalIgnoreCase);
            var nullChanged = prev.IsNullable != col.IsNullable;
            if (typeChanged || nullChanged)
            {
                var oldDesc = prev.DataType + (prev.IsNullable ? "?" : string.Empty);
                var newDesc = col.DataType + (col.IsNullable ? "?" : string.Empty);
                notes.Add($"~col {col.Name} ({oldDesc}->{newDesc})");
            }
        }

        return notes;
    }
}
