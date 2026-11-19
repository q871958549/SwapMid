using System;
using System.Collections.Generic;
using System.Linq;

namespace SwapMid.DataIntegration;

/// <summary>
/// Push Webhook CDC 载荷自动 Schema 推导辅助类：
/// 从已验签事件批次的 <c>Data</c>/<c>OldData</c> 字典推导 <see cref="CdcCapturedTableSchema"/>，
/// 支持首批自动锁定 Schema 基线（激活 Data Contract 必填列校验）及后续批次 Schema 漂移检测。
/// </summary>
internal static class CdcWebhookSchemaInferenceHelper
{
    internal static List<CdcCapturedTableSchema> InferSchemas(
        IReadOnlyList<PgOutputCapturedChange> changes)
    {
        var tableColumns = new Dictionary<(string Schema, string Table), Dictionary<string, (string DataType, bool SeenNull)>>(
            new SchemaTableComparer());

        foreach (var change in changes)
        {
            var key = (change.Schema, change.Table);
            if (!tableColumns.TryGetValue(key, out var colMap))
            {
                colMap = new Dictionary<string, (string DataType, bool SeenNull)>(StringComparer.OrdinalIgnoreCase);
                tableColumns[key] = colMap;
            }

            InspectRow(change.Data, colMap);
            InspectRow(change.OldData, colMap);
        }

        var result = new List<CdcCapturedTableSchema>(tableColumns.Count);
        foreach (var kv in tableColumns)
        {
            if (kv.Value.Count == 0)
            {
                continue;
            }

            var cols = kv.Value
                .OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CdcCapturedTableColumn(c.Key, c.Value.DataType, IsNullable: true))
                .ToArray();
            result.Add(new CdcCapturedTableSchema(kv.Key.Schema, kv.Key.Table, cols));
        }

        return result;
    }

    private static void InspectRow(
        IReadOnlyDictionary<string, object?>? row,
        Dictionary<string, (string DataType, bool SeenNull)> colMap)
    {
        if (row == null)
        {
            return;
        }

        foreach (var kv in row)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            var inferred = InferLogicalType(kv.Value);
            if (!colMap.TryGetValue(kv.Key, out var existing))
            {
                colMap[kv.Key] = (inferred, kv.Value == null);
                continue;
            }

            var merged = MergeTypes(existing.DataType, inferred);
            colMap[kv.Key] = (merged, existing.SeenNull || kv.Value == null);
        }
    }

    private static string InferLogicalType(object? value)
        => value switch
        {
            null => "unknown",
            bool => "boolean",
            long or int => "bigint",
            decimal or double or float => "numeric",
            string s when s.StartsWith('{') || s.StartsWith('[') => "jsonb",
            _ => "text",
        };

    private static string MergeTypes(string current, string next)
    {
        if (current == "unknown") return next;
        if (next == "unknown" || string.Equals(current, next, StringComparison.Ordinal)) return current;
        if ((current == "bigint" && next == "numeric") || (current == "numeric" && next == "bigint")) return "numeric";
        return "text";
    }

    private sealed class SchemaTableComparer : IEqualityComparer<(string Schema, string Table)>
    {
        public bool Equals((string Schema, string Table) x, (string Schema, string Table) y)
            => string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Schema, string Table) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table));
    }
}
