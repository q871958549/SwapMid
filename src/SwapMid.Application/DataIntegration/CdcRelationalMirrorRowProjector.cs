using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// CDC 关系表行级镜像的显式字段映射、同批事件折叠、TOAST 恢复与软删除墓碑列投影辅助类。
/// </summary>
internal static class CdcRelationalMirrorRowProjector
{
    private const string ToastUnchangedSentinel = "__toast_unchanged__";

    internal sealed record CoalescedRow(
        bool IsDelete,
        bool HasFullPayload,
        Dictionary<string, object?>? Data,
        object?[] KeyValues,
        DateTime CommitTimestampUtc);

    internal static List<CoalescedRow> CoalesceChangesByKey(
        IReadOnlyList<PgOutputCapturedChange> changes,
        IReadOnlyList<string> keyColumns,
        CdcDeletePolicy deletePolicy,
        IReadOnlyList<CdcFieldMappingRule>? fieldMappings = null)
    {
        var mappings = fieldMappings ?? Array.Empty<CdcFieldMappingRule>();
        var keySourceMap = BuildKeySourceMap(keyColumns, mappings);

        var order = new List<string>(changes.Count);
        var map = new Dictionary<string, CoalescedRow>(StringComparer.Ordinal);

        foreach (var change in changes)
        {
            var isDelete = string.Equals(change.Operation, "delete", StringComparison.OrdinalIgnoreCase);
            if (isDelete && deletePolicy == CdcDeletePolicy.Ignore)
            {
                continue;
            }

            var rawPayload = isDelete
                ? (change.OldData ?? change.Data)
                : (change.Data ?? change.OldData);
            if (rawPayload == null || rawPayload.Count == 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "CDC 变更载荷为空，无法提取业务主键进行行级物化。");
            }

            var keyValues = ExtractKeyValues(rawPayload, keyColumns, keySourceMap);
            var compositeKey = BuildCompositeKeyString(keyValues);
            map.TryGetValue(compositeKey, out var previous);

            if (isDelete)
            {
                if (!map.ContainsKey(compositeKey))
                {
                    order.Add(compositeKey);
                }

                if (deletePolicy == CdcDeletePolicy.SoftDelete &&
                    previous is { HasFullPayload: true, Data: not null })
                {
                    // 同批次先 Insert/Update 后 Delete：保留完整业务列并携带墓碑标记进 Upsert，保证目标库不因 NOT NULL 缺列失败。
                    map[compositeKey] = new CoalescedRow(
                        IsDelete: true,
                        HasFullPayload: true,
                        Data: previous.Data,
                        KeyValues: keyValues,
                        CommitTimestampUtc: change.CommitTimestampUtc);
                }
                else
                {
                    map[compositeKey] = new CoalescedRow(
                        IsDelete: true,
                        HasFullPayload: false,
                        Data: null,
                        KeyValues: keyValues,
                        CommitTimestampUtc: change.CommitTimestampUtc);
                }

                continue;
            }

            var projectedData = RemapToTarget(rawPayload, mappings);
            var projectedOld = change.OldData == null ? null : RemapToTarget(change.OldData, mappings);
            ResolveToastSentinels(projectedData, previous, projectedOld);

            if (!map.ContainsKey(compositeKey))
            {
                order.Add(compositeKey);
            }

            map[compositeKey] = new CoalescedRow(
                IsDelete: false,
                HasFullPayload: true,
                Data: projectedData,
                KeyValues: keyValues,
                CommitTimestampUtc: change.CommitTimestampUtc);
        }

        var result = new List<CoalescedRow>(order.Count);
        foreach (var key in order)
        {
            result.Add(map[key]);
        }

        return result;
    }

    internal static string[] ResolveUpsertColumns(
        IReadOnlyList<PreflightColumnDto> targetSchema,
        IReadOnlyDictionary<string, object?> sampleRow,
        IReadOnlyList<CdcFieldMappingRule> fieldMappings,
        CdcDeletePolicy deletePolicy,
        string? canonicalSoftDeleteColumn,
        string? canonicalSoftDeleteTimestampColumn)
    {
        var targetSet = targetSchema.ToDictionary(c => c.Name, c => c.Name, StringComparer.OrdinalIgnoreCase);
        var selected = new List<string>(targetSchema.Count);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (fieldMappings.Count > 0)
        {
            foreach (var rule in fieldMappings)
            {
                if (!targetSet.TryGetValue(rule.TargetColumn, out var canonicalTarget))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"显式字段映射目标列 \"{rule.TargetColumn}\" 在目标表中不存在。");
                }

                if (added.Add(canonicalTarget))
                {
                    selected.Add(canonicalTarget);
                }
            }
        }
        else
        {
            foreach (var tc in targetSchema)
            {
                if (string.Equals(tc.Name, canonicalSoftDeleteColumn, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tc.Name, canonicalSoftDeleteTimestampColumn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (sampleRow.ContainsKey(tc.Name) && added.Add(tc.Name))
                {
                    selected.Add(tc.Name);
                }
            }
        }

        if (deletePolicy == CdcDeletePolicy.SoftDelete)
        {
            if (!string.IsNullOrWhiteSpace(canonicalSoftDeleteColumn) && added.Add(canonicalSoftDeleteColumn))
            {
                selected.Add(canonicalSoftDeleteColumn);
            }

            if (!string.IsNullOrWhiteSpace(canonicalSoftDeleteTimestampColumn) && added.Add(canonicalSoftDeleteTimestampColumn))
            {
                selected.Add(canonicalSoftDeleteTimestampColumn);
            }
        }

        if (selected.Count == 0)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "源表捕获列与目标关系表列无交集，无法物化写入。");
        }

        return selected.ToArray();
    }

    internal static List<object?[]> BuildProjectedUpsertRows(
        IReadOnlyList<CoalescedRow> upsertRows,
        IReadOnlyList<string> projectedCols,
        IReadOnlyDictionary<string, PreflightColumnDto> targetColByName,
        CdcDeletePolicy deletePolicy,
        string? canonicalSoftDeleteColumn,
        string? canonicalSoftDeleteTimestampColumn,
        string normalizedProvider)
    {
        var list = new List<object?[]>(upsertRows.Count);
        foreach (var entry in upsertRows)
        {
            var data = entry.Data!;
            var row = new object?[projectedCols.Count];
            for (var c = 0; c < projectedCols.Count; c++)
            {
                var colName = projectedCols[c];
                if (deletePolicy == CdcDeletePolicy.SoftDelete &&
                    string.Equals(colName, canonicalSoftDeleteColumn, StringComparison.OrdinalIgnoreCase))
                {
                    row[c] = CdcMirrorValueCoercer.CoerceSoftDeleteFlag(
                        entry.IsDelete,
                        targetColByName[colName].DataType,
                        normalizedProvider);
                    continue;
                }

                if (deletePolicy == CdcDeletePolicy.SoftDelete &&
                    !string.IsNullOrWhiteSpace(canonicalSoftDeleteTimestampColumn) &&
                    string.Equals(colName, canonicalSoftDeleteTimestampColumn, StringComparison.OrdinalIgnoreCase))
                {
                    row[c] = entry.IsDelete
                        ? CdcMirrorValueCoercer.CoerceSoftDeleteTimestamp(
                            entry.CommitTimestampUtc,
                            targetColByName[colName].DataType)
                        : null;
                    continue;
                }

                data.TryGetValue(colName, out var rawVal);
                if (rawVal is string str && string.Equals(str, ToastUnchangedSentinel, StringComparison.Ordinal))
                {
                    throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                        .WithData("Reason", $"UPDATE 事件中大字段列 \"{colName}\" 未包含新值，请对上游源表启用 REPLICA IDENTITY FULL。");
                }

                row[c] = CdcMirrorValueCoercer.Coerce(rawVal, targetColByName[colName].DataType);
            }

            list.Add(row);
        }

        return list;
    }

    internal static List<object?[]> BuildDeleteKeyTuples(
        IReadOnlyList<CoalescedRow> deleteEntries,
        IReadOnlyList<string> canonicalKeyColumns,
        IReadOnlyDictionary<string, PreflightColumnDto> targetColByName)
    {
        var tuples = new List<object?[]>(deleteEntries.Count);
        foreach (var entry in deleteEntries)
        {
            tuples.Add(CoerceKeyTuple(entry.KeyValues, canonicalKeyColumns, targetColByName));
        }

        return tuples;
    }

    internal static List<EtlTargetAtomicWriter.CdcMirrorSoftDeleteTuple> BuildSoftDeleteTuples(
        IReadOnlyList<CoalescedRow> softDeleteEntries,
        IReadOnlyList<string> canonicalKeyColumns,
        IReadOnlyDictionary<string, PreflightColumnDto> targetColByName,
        string canonicalSoftDeleteColumn,
        string? canonicalSoftDeleteTimestampColumn,
        string normalizedProvider)
    {
        var flagDataType = targetColByName[canonicalSoftDeleteColumn].DataType;
        var tsDataType = !string.IsNullOrWhiteSpace(canonicalSoftDeleteTimestampColumn)
            ? targetColByName[canonicalSoftDeleteTimestampColumn].DataType
            : null;
        var flagValue = CdcMirrorValueCoercer.CoerceSoftDeleteFlag(true, flagDataType, normalizedProvider);

        var tuples = new List<EtlTargetAtomicWriter.CdcMirrorSoftDeleteTuple>(softDeleteEntries.Count);
        foreach (var entry in softDeleteEntries)
        {
            var coercedKeys = CoerceKeyTuple(entry.KeyValues, canonicalKeyColumns, targetColByName);
            var tsValue = !string.IsNullOrWhiteSpace(canonicalSoftDeleteTimestampColumn)
                ? CdcMirrorValueCoercer.CoerceSoftDeleteTimestamp(entry.CommitTimestampUtc, tsDataType)
                : null;
            tuples.Add(new EtlTargetAtomicWriter.CdcMirrorSoftDeleteTuple(coercedKeys, flagValue, tsValue));
        }

        return tuples;
    }

    private static object?[] CoerceKeyTuple(
        object?[] rawKeys,
        IReadOnlyList<string> canonicalKeyColumns,
        IReadOnlyDictionary<string, PreflightColumnDto> targetColByName)
    {
        var coerced = new object?[canonicalKeyColumns.Count];
        for (var k = 0; k < canonicalKeyColumns.Count; k++)
        {
            coerced[k] = CdcMirrorValueCoercer.Coerce(rawKeys[k], targetColByName[canonicalKeyColumns[k]].DataType);
        }

        return coerced;
    }

    private static Dictionary<string, string> BuildKeySourceMap(
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<CdcFieldMappingRule> fieldMappings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fieldMappings.Count == 0)
        {
            foreach (var key in keyColumns)
            {
                map[key] = key;
            }

            return map;
        }

        var byTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in fieldMappings)
        {
            byTarget[rule.TargetColumn] = rule.SourceColumn;
        }

        foreach (var key in keyColumns)
        {
            map[key] = byTarget.TryGetValue(key, out var srcCol) ? srcCol : key;
        }

        return map;
    }

    private static Dictionary<string, object?> RemapToTarget(
        IReadOnlyDictionary<string, object?> sourcePayload,
        IReadOnlyList<CdcFieldMappingRule> mappings)
    {
        if (mappings.Count == 0)
        {
            return new Dictionary<string, object?>(sourcePayload, StringComparer.OrdinalIgnoreCase);
        }

        var srcLookup = new Dictionary<string, object?>(sourcePayload, StringComparer.OrdinalIgnoreCase);
        var projected = new Dictionary<string, object?>(mappings.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in mappings)
        {
            if (srcLookup.TryGetValue(rule.SourceColumn, out var val))
            {
                projected[rule.TargetColumn] = val;
            }
        }

        return projected;
    }

    private static object?[] ExtractKeyValues(
        IReadOnlyDictionary<string, object?> sourcePayload,
        IReadOnlyList<string> keyColumns,
        IReadOnlyDictionary<string, string> keySourceMap)
    {
        var lookup = new Dictionary<string, object?>(sourcePayload, StringComparer.OrdinalIgnoreCase);
        var values = new object?[keyColumns.Count];
        for (var i = 0; i < keyColumns.Count; i++)
        {
            var targetKey = keyColumns[i];
            var sourceKey = keySourceMap.TryGetValue(targetKey, out var mappedSrc) ? mappedSrc : targetKey;
            if (!lookup.TryGetValue(sourceKey, out var value) || value == null)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"CDC 变更事件缺少非空业务主键源列 \"{sourceKey}\"（映射目标主键 \"{targetKey}\"）。");
            }

            values[i] = value;
        }

        return values;
    }

    private static void ResolveToastSentinels(
        Dictionary<string, object?> current,
        CoalescedRow? previous,
        IReadOnlyDictionary<string, object?>? oldData)
    {
        var oldLookup = oldData == null
            ? null
            : new Dictionary<string, object?>(oldData, StringComparer.OrdinalIgnoreCase);

        foreach (var colName in current.Keys.ToArray())
        {
            if (current[colName] is string s && string.Equals(s, ToastUnchangedSentinel, StringComparison.Ordinal))
            {
                if (oldLookup != null && oldLookup.TryGetValue(colName, out var oldVal) &&
                    !(oldVal is string os && string.Equals(os, ToastUnchangedSentinel, StringComparison.Ordinal)))
                {
                    current[colName] = oldVal;
                }
                else if (previous is { Data: not null } && previous.Data.TryGetValue(colName, out var prevVal))
                {
                    current[colName] = prevVal;
                }
            }
        }
    }

    private static string BuildCompositeKeyString(object?[] keyValues)
    {
        var parts = new string[keyValues.Length];
        for (var i = 0; i < keyValues.Length; i++)
        {
            parts[i] = keyValues[i] switch
            {
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => keyValues[i]?.ToString() ?? string.Empty,
            };
        }

        return string.Join('\u001f', parts);
    }
}
