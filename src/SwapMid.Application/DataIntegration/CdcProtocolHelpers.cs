using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NpgsqlTypes;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;

namespace SwapMid.DataIntegration;

internal readonly record struct MonitoredTableSpec(string Schema, string Table)
{
    public string FullName => $"{Schema}.{Table}";
}

/// <summary>
/// PostgreSQL 逻辑复制（pgoutput）协议辅助校验与序列化器（纯函数，无外部 I/O）。
/// </summary>
internal static partial class CdcProtocolHelpers
{
    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.Compiled)]
    private static partial Regex ReplicationIdentifierRegex();

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.Compiled)]
    private static partial Regex TableIdentifierRegex();

    internal static string ValidateReplicationIdentifier(string? raw, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"{fieldName} 不能为空。");
        }

        var trimmed = raw.Trim();
        if (!ReplicationIdentifierRegex().IsMatch(trimmed))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData(
                    "Reason",
                    $"{fieldName}（{trimmed}）不合法，仅允许小写字母、数字和下划线且以字母或下划线开头，长度不超过 63。");
        }

        return trimmed;
    }

    internal static IReadOnlyList<MonitoredTableSpec> ParseMonitoredTables(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<MonitoredTableSpec>();
        }

        var tokens = raw.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > CdcStreamConsts.MaxMonitoredTableCount)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"监听表数量不能超过 {CdcStreamConsts.MaxMonitoredTableCount} 张。");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var specs = new List<MonitoredTableSpec>(tokens.Length);
        foreach (var token in tokens)
        {
            var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string schema;
            string table;
            if (parts.Length == 1)
            {
                schema = "public";
                table = parts[0];
            }
            else if (parts.Length == 2)
            {
                schema = parts[0];
                table = parts[1];
            }
            else
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"监听表名格式无效（{token}），请使用 schema.table 格式。");
            }

            if (!TableIdentifierRegex().IsMatch(schema) || !TableIdentifierRegex().IsMatch(table))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"监听表标识符不合法（{token}），仅允许字母、数字和下划线。");
            }

            var key = $"{schema}.{table}";
            if (seen.Add(key))
            {
                specs.Add(new MonitoredTableSpec(schema, table));
            }
        }

        return specs;
    }

    internal static bool ShouldCaptureRelation(
        string schema,
        string table,
        IReadOnlyList<MonitoredTableSpec> monitoredTables)
    {
        if (monitoredTables.Count == 0)
        {
            return true;
        }

        foreach (var spec in monitoredTables)
        {
            if (string.Equals(spec.Schema, schema, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(spec.Table, table, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static NpgsqlLogSequenceNumber? TryParseLsn(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return NpgsqlLogSequenceNumber.TryParse(raw.Trim(), out var parsed)
            ? parsed
            : null;
    }

    internal static NpgsqlLogSequenceNumber ValidateLsn(string raw)
    {
        if (!NpgsqlLogSequenceNumber.TryParse(raw.Trim(), out var parsed))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"WAL LSN 格式无效（{raw}），示例：0/16A2B88。");
        }

        return parsed;
    }

    internal static int CompareLsn(string? left, string? right)
    {
        var leftVal = TryParseLsn(left);
        var rightVal = TryParseLsn(right);
        if (leftVal == null && rightVal == null) return 0;
        if (leftVal == null) return -1;
        if (rightVal == null) return 1;
        return ((ulong)leftVal.Value).CompareTo((ulong)rightVal.Value);
    }

    internal static string BuildCreatePublicationSql(
        string publicationName,
        IReadOnlyList<MonitoredTableSpec> monitoredTables)
    {
        var quotedPub = QuotePgIdentifier(publicationName);
        if (monitoredTables.Count == 0)
        {
            return $"CREATE PUBLICATION {quotedPub} FOR ALL TABLES";
        }

        var tableList = new string[monitoredTables.Count];
        for (var i = 0; i < monitoredTables.Count; i++)
        {
            tableList[i] = $"{QuotePgIdentifier(monitoredTables[i].Schema)}.{QuotePgIdentifier(monitoredTables[i].Table)}";
        }

        return $"CREATE PUBLICATION {quotedPub} FOR TABLE {string.Join(", ", tableList)}";
    }

    internal static string QuotePgIdentifier(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"")}\"";

    internal static string BuildEventSubject(string schema, string table, string operation)
        => $"cdc.{schema.ToLowerInvariant()}.{table.ToLowerInvariant()}.{operation.ToLowerInvariant()}";

    internal static Guid BuildDeterministicEventId(
        Guid streamId,
        string lsn,
        int sequenceIndex,
        string operation,
        string schema,
        string table)
    {
        return DeterministicEventId.Create(
            $"swapmid-cdc-v1:{streamId:D}:{lsn}:{sequenceIndex}:{operation.ToLowerInvariant()}:{schema.ToLowerInvariant()}.{table.ToLowerInvariant()}");
    }

    internal static Guid BuildDeterministicPipelineRunId(Guid streamId, string batchEndLsn)
    {
        return DeterministicEventId.Create($"swapmid-cdc-pipeline-v1:{streamId:D}:{batchEndLsn}");
    }

    internal static string BuildEventPayloadJson(
        Guid streamId,
        string streamName,
        string schema,
        string table,
        string operation,
        string lsn,
        DateTime commitTimestampUtc,
        IReadOnlyDictionary<string, object?>? data,
        IReadOnlyDictionary<string, object?>? oldData)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["streamId"] = streamId,
            ["streamName"] = streamName,
            ["schema"] = schema,
            ["table"] = table,
            ["operation"] = operation,
            ["lsn"] = lsn,
            ["commitTimestampUtc"] = commitTimestampUtc.ToUniversalTime().ToString("O"),
            ["changedColumns"] = ComputeChangedColumns(data, oldData),
            ["data"] = data,
            ["old"] = oldData,
        };

        return JsonSerializer.Serialize(envelope);
    }

    internal static IReadOnlyList<string>? ComputeChangedColumns(
        IReadOnlyDictionary<string, object?>? data,
        IReadOnlyDictionary<string, object?>? oldData)
    {
        if (data == null || oldData == null)
        {
            return null;
        }

        var allKeys = new SortedSet<string>(data.Keys, StringComparer.Ordinal);
        foreach (var oldKey in oldData.Keys)
        {
            allKeys.Add(oldKey);
        }

        var changed = new List<string>();
        foreach (var key in allKeys)
        {
            var hasNew = data.TryGetValue(key, out var newVal);
            var hasOld = oldData.TryGetValue(key, out var oldVal);

            if (hasNew && newVal is string marker && marker == "__toast_unchanged__")
            {
                continue;
            }

            if (!hasNew || !hasOld)
            {
                changed.Add(key);
                continue;
            }

            if (!AreSerializedValuesEqual(oldVal, newVal))
            {
                changed.Add(key);
            }
        }

        return changed;
    }

    internal static string MapReplicaIdentityCode(string? code)
        => (code ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "d" => "default",
            "f" => "full",
            "i" => "index",
            "n" => "nothing",
            _ => "unknown",
        };

    private static bool AreSerializedValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(
            JsonSerializer.Serialize(left),
            JsonSerializer.Serialize(right),
            StringComparison.Ordinal);
    }

    internal static IReadOnlyList<string> ParseSinkKeyColumns(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        var tokens = raw.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > CdcStreamConsts.MaxTargetSinkKeyColumnCount)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"外部接收端分区键数量不能超过 {CdcStreamConsts.MaxTargetSinkKeyColumnCount} 列。");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!EtlTransformConfig.IsValidColumnName(token))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", $"外部接收端分区键列名无效（{token}），仅允许字母、数字和下划线。");
            }

            if (seen.Add(token))
            {
                columns.Add(token);
            }
        }

        return columns;
    }

    internal static string? BuildPartitionKey(
        string schema,
        string table,
        IReadOnlyList<string> keyColumns,
        IReadOnlyDictionary<string, object?>? data,
        IReadOnlyDictionary<string, object?>? oldData)
    {
        if (keyColumns.Count == 0)
        {
            return null;
        }

        var source = data ?? oldData;
        if (source == null || source.Count == 0)
        {
            return $"{schema.ToLowerInvariant()}.{table.ToLowerInvariant()}";
        }

        var parts = new string[keyColumns.Count];
        for (var i = 0; i < keyColumns.Count; i++)
        {
            var col = keyColumns[i];
            var found = TryGetColumnValue(source, col, out var val);
            if (!found && data != null && oldData != null)
            {
                TryGetColumnValue(oldData, col, out val);
            }

            var valText = val switch
            {
                null => "null",
                string s when s == "__toast_unchanged__" => "__toast__",
                _ => Convert.ToString(val, CultureInfo.InvariantCulture) ?? "null",
            };
            parts[i] = $"{col}={valText}";
        }

        return $"{schema.ToLowerInvariant()}.{table.ToLowerInvariant()}:{string.Join("|", parts)}";
    }

    private static bool TryGetColumnValue(
        IReadOnlyDictionary<string, object?> source,
        string columnName,
        out object? value)
    {
        if (source.TryGetValue(columnName, out value))
        {
            return true;
        }

        foreach (var entry in source)
        {
            if (string.Equals(entry.Key, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
