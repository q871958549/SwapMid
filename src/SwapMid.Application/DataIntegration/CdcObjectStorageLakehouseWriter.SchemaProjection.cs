using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SwapMid.DataIntegration;

internal static partial class CdcObjectStorageLakehouseWriter
{
    internal sealed record ProjectedIcebergBatch(
        string[] Columns,
        Type[] ClrTypes,
        List<object?[]> Rows,
        int[]? FieldIds);

    /// <summary>
    /// 探测当前 Apache Iceberg v2 表是否已演进出自定义提升列（<c>FieldId &gt; 12</c>）；
    /// 若存在，则自动从每条 CDC 变更的 <c>After</c>（删除时回退 <c>Before</c>）按当前列名与历史别名（<c>schema.name-mapping.default</c>）
    /// 安全提取并类型转换到 Parquet 一等列，并附带精确的不可变 <c>FieldId</c> 数组。
    /// </summary>
    internal static async Task<ProjectedIcebergBatch> ProjectIcebergPromotedColumnsAsync(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ObjectStorageSinkConfig config,
        string destination,
        IReadOnlyList<PgOutputCapturedChange> changes,
        string[] baseColumns,
        Type[] baseClrTypes,
        List<object?[]> baseRows,
        CancellationToken cancellationToken)
    {
        var hintSeq = await IcebergV2CommitWriter.TryReadVersionHintAsync(
            httpClientFactory, configuration, config, destination, cancellationToken);
        if (hintSeq is not > 0)
        {
            return new ProjectedIcebergBatch(baseColumns, baseClrTypes, baseRows, null);
        }

        var metadataJson = await TryGetObjectStorageJsonAsync(
            httpClientFactory, configuration, config, config.BuildIcebergMetadataKey(destination, hintSeq.Value), cancellationToken);
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new ProjectedIcebergBatch(baseColumns, baseClrTypes, baseRows, null);
        }

        var parsed = IcebergV2SchemaEvolutionService.ParseActiveSchema(metadataJson);
        var promoted = parsed.Fields
            .Where(f => f.FieldId > IcebergV2SchemaEvolutionService.MaxCoreEnvelopeFieldId)
            .OrderBy(f => f.FieldId)
            .ToList();
        if (promoted.Count == 0)
        {
            return new ProjectedIcebergBatch(baseColumns, baseClrTypes, baseRows, null);
        }

        var totalCols = baseColumns.Length + promoted.Count;
        var nextCols = new string[totalCols];
        var nextTypes = new Type[totalCols];
        var fieldIds = new int[totalCols];
        for (var i = 0; i < baseColumns.Length; i++)
        {
            nextCols[i] = baseColumns[i];
            nextTypes[i] = baseClrTypes[i];
            fieldIds[i] = i + 1;
        }

        var lookupNamesPerPromoted = new List<string[]>(promoted.Count);
        for (var p = 0; p < promoted.Count; p++)
        {
            var pf = promoted[p];
            var idx = baseColumns.Length + p;
            nextCols[idx] = pf.Name;
            nextTypes[idx] = MapIcebergTypeToClr(pf.Type);
            fieldIds[idx] = pf.FieldId;

            var names = new List<string> { pf.Name };
            if (parsed.NameAliasesByFieldId.TryGetValue(pf.FieldId, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    if (!names.Any(n => string.Equals(n, alias, StringComparison.OrdinalIgnoreCase)))
                    {
                        names.Add(alias);
                    }
                }
            }

            lookupNamesPerPromoted.Add(names.ToArray());
        }

        var nextRows = new List<object?[]>(baseRows.Count);
        for (var r = 0; r < baseRows.Count; r++)
        {
            var srcRow = baseRows[r];
            var extRow = new object?[totalCols];
            Array.Copy(srcRow, extRow, Math.Min(srcRow.Length, baseColumns.Length));
            var payload = r < changes.Count ? (changes[r].Data ?? changes[r].OldData) : null;
            if (payload != null)
            {
                for (var p = 0; p < promoted.Count; p++)
                {
                    var raw = TryFindPayloadValue(payload, lookupNamesPerPromoted[p]);
                    extRow[baseColumns.Length + p] = CoerceToIcebergType(raw, promoted[p].Type);
                }
            }

            nextRows.Add(extRow);
        }

        return new ProjectedIcebergBatch(nextCols, nextTypes, nextRows, fieldIds);
    }

    private static object? TryFindPayloadValue(IReadOnlyDictionary<string, object?> payload, string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            if (payload.TryGetValue(name, out var direct)) return direct;
        }

        foreach (var kv in payload)
        {
            foreach (var name in candidateNames)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return kv.Value;
                }
            }
        }

        return null;
    }

    private static Type MapIcebergTypeToClr(string icebergType) => icebergType.ToLowerInvariant() switch
    {
        "long" => typeof(long),
        "double" => typeof(double),
        "boolean" => typeof(bool),
        "timestamptz" => typeof(DateTime),
        _ => typeof(string),
    };

    private static object? CoerceToIcebergType(object? raw, string icebergType)
    {
        if (raw is null or DBNull) return null;
        if (raw is JsonElement je)
        {
            raw = UnwrapJsonElement(je);
            if (raw is null) return null;
        }

        try
        {
            return icebergType.ToLowerInvariant() switch
            {
                "long" => Convert.ToInt64(raw, CultureInfo.InvariantCulture),
                "double" => Convert.ToDouble(raw, CultureInfo.InvariantCulture),
                "boolean" => raw is bool b
                    ? b
                    : (bool.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var pb) ? pb : Convert.ToBoolean(raw, CultureInfo.InvariantCulture)),
                "timestamptz" => raw is DateTime dt
                    ? (dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime())
                    : (raw is DateTimeOffset dto
                        ? dto.UtcDateTime
                        : DateTimeOffset.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).UtcDateTime),
                _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
            };
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static object? UnwrapJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => el.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        _ => el.GetRawText(),
    };
}
