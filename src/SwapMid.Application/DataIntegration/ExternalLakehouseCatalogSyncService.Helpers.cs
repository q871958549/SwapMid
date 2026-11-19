using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Volo.Abp;

namespace SwapMid.DataIntegration;

public sealed partial class ExternalLakehouseCatalogSyncService
{
    private static void EnsureHttpSuccess(RestCallResult result, string operationName)
    {
        if (result.IsSuccess)
        {
            return;
        }

        var redacted = SinkConfigContractHelpers.RedactDiagnosticText(result.BodySnippet);
        if (redacted.Length > 240)
        {
            redacted = redacted[..240];
        }

        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", $"{operationName}失败（HTTP {(int)result.StatusCode}）：{redacted}");
    }

    private static Dictionary<string, string> BuildMetadataProperties(
        CdcStreamDefinition stream,
        string locationUri,
        long? version,
        long? icebergSnapshotId,
        string? icebergMetadataUri)
    {
        var formatTag = stream.LakehouseTableFormat switch
        {
            LakehouseTableFormat.DeltaLake => "delta",
            LakehouseTableFormat.ApacheIceberg => "iceberg",
            _ => "swapmid-manifest",
        };
        var props = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["swapmid.stream_id"] = stream.Id.ToString("D"),
            ["swapmid.stream_name"] = stream.Name,
            ["swapmid.table_format"] = formatTag,
            ["swapmid.location"] = locationUri,
        };
        if (version.HasValue)
        {
            var versionKey = stream.LakehouseTableFormat == LakehouseTableFormat.ApacheIceberg
                ? "swapmid.iceberg_sequence_number"
                : "swapmid.delta_version";
            props[versionKey] = version.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (icebergSnapshotId.HasValue)
        {
            props["swapmid.iceberg_snapshot_id"] = icebergSnapshotId.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(icebergMetadataUri))
        {
            props["metadata-location"] = icebergMetadataUri;
            props["swapmid.metadata_location"] = icebergMetadataUri;
        }

        if (!string.IsNullOrWhiteSpace(stream.CheckpointLsn))
        {
            props["swapmid.checkpoint_lsn"] = stream.CheckpointLsn;
        }

        if (!string.IsNullOrWhiteSpace(stream.SchemaBaselineHash))
        {
            props["swapmid.schema_hash"] = stream.SchemaBaselineHash;
        }

        return props;
    }

    private static IReadOnlyList<CatalogColumnDescriptor> ResolveCatalogColumns(CdcStreamDefinition stream)
    {
        _ = stream;
        return
        [
            new("event_id", "string", "string"),
            new("stream_id", "string", "string"),
            new("stream_name", "string", "string"),
            new("source_schema", "string", "string"),
            new("source_table", "string", "string"),
            new("op", "string", "string"),
            new("lsn", "string", "string"),
            new("commit_timestamp_utc", "timestamp", "timestamptz"),
            new("changed_columns", "string", "string"),
            new("after_json", "string", "string"),
            new("before_json", "string", "string"),
        ];
    }

    private static (string Catalog, string Schema, string Table) ResolveUnityThreeLevelName(
        CatalogRestConfig restCfg,
        CdcStreamDefinition stream)
    {
        var ns = string.IsNullOrWhiteSpace(stream.CatalogNamespace) ? "default" : stream.CatalogNamespace.Trim();
        var dotIdx = ns.IndexOf('.', StringComparison.Ordinal);
        string catalogName;
        string schemaName;
        if (dotIdx > 0 && dotIdx < ns.Length - 1)
        {
            catalogName = ns[..dotIdx];
            schemaName = ns[(dotIdx + 1)..];
        }
        else
        {
            catalogName = string.IsNullOrWhiteSpace(restCfg.CatalogName) ? "swapmid" : restCfg.CatalogName!;
            schemaName = ns;
        }

        var tableName = string.IsNullOrWhiteSpace(stream.CatalogTableName) ? stream.Name : stream.CatalogTableName.Trim();
        return (catalogName, schemaName, tableName);
    }

    private static string BuildDisplayIdentifier(CdcStreamDefinition stream)
        => string.IsNullOrWhiteSpace(stream.CatalogNamespace) || string.IsNullOrWhiteSpace(stream.CatalogTableName)
            ? stream.Name
            : $"{stream.CatalogNamespace}.{stream.CatalogTableName}";

    private static LatestLakehouseMeta TryReadLatestLakehouseMeta(string? latestJson)
    {
        if (string.IsNullOrWhiteSpace(latestJson))
        {
            return default;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument>(
                latestJson,
                CdcObjectStorageLakehouseWriter.JsonOptions);
            return new LatestLakehouseMeta(
                doc?.LatestDeltaVersion,
                doc?.LatestIcebergSequenceNumber,
                doc?.LatestIcebergSnapshotId,
                doc?.LatestIcebergMetadataLocation);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private readonly record struct LatestLakehouseMeta(
        long? DeltaVersion,
        long? IcebergSequenceNumber,
        long? IcebergSnapshotId,
        string? IcebergMetadataLocation);

    private static CatalogRestConfig ParseCatalogRestConfig(string? rawConfig)
    {
        if (!DataSourceConfigJson.TryParseObject(rawConfig, out var root))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigInvalid)
                .WithData("Reason", "Catalog RestApi 数据源 JSON 配置格式无效。");
        }

        var rawUrl = DataSourceConfigJson.ReadOptionalStringStrict(root, "baseUrl", "endpoint", "url")?.Trim();
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.DataSourceConfigInvalid)
                .WithData("Reason", "Catalog RestApi 数据源必须包含有效的 HTTP/HTTPS baseUrl（或 endpoint）。");
        }

        var token = DataSourceConfigJson.ReadOptionalStringStrict(root, "bearerToken", "token", "apiKey")?.Trim();
        var apiKeyHeader = DataSourceConfigJson.ReadOptionalStringStrict(root, "apiKeyHeader")?.Trim();
        var catalogName = DataSourceConfigJson.ReadOptionalStringStrict(root, "catalogName")?.Trim();
        var prefix = DataSourceConfigJson.ReadOptionalStringStrict(root, "prefix", "warehouse")?.Trim();

        var extraHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (DataSourceConfigJson.TryFindProperty(root, "headers", out _, out var headersNode) &&
            headersNode is JsonObject headersObj)
        {
            foreach (var kv in headersObj)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value is JsonValue hv && hv.TryGetValue<string>(out var val))
                {
                    extraHeaders[kv.Key] = val;
                }
            }
        }

        return new CatalogRestConfig(baseUri, token, apiKeyHeader, catalogName, prefix, extraHeaders);
    }

    private static Uri BuildEndpointUri(Uri baseUri, string relativeApiPath)
    {
        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(baseUri)
        {
            Path = basePath + relativeApiPath,
            Query = string.Empty,
        };
        return builder.Uri;
    }

    private static string ToMetricLabel(LakehouseCatalogType catalogType)
        => catalogType == LakehouseCatalogType.UnityCatalogRest ? "unity_catalog" : "iceberg_rest";

    private sealed record CatalogRestConfig(
        Uri BaseUri,
        string? BearerToken,
        string? ApiKeyHeader,
        string? CatalogName,
        string? Prefix,
        IReadOnlyDictionary<string, string> ExtraHeaders);

    private sealed record CatalogColumnDescriptor(string Name, string SparkType, string IcebergType);

    private sealed record RestCallResult(bool IsSuccess, HttpStatusCode StatusCode, string BodySnippet);
}
