using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SwapMid.DataIntegration;

/// <summary>S3 / MinIO 对象存储目标配置契约、凭据脱敏与对象键规范。</summary>
internal static partial class ObjectStorageSinkConfigContract
{
    internal static DataSourceProtectedConfig SanitizeForRead(JsonObject root)
    {
        var sanitized = new JsonObject();
        var hasSecret = false;

        var rawProvider = DataSourceConfigJson.ReadOptionalStringStrict(root, "provider")?.Trim().ToLowerInvariant();
        if (rawProvider is "s3" or "minio" or "s3-parquet")
        {
            sanitized["provider"] = rawProvider == "s3-parquet" ? "s3" : rawProvider;
        }
        else if (!string.IsNullOrWhiteSpace(rawProvider))
        {
            hasSecret = true;
        }

        if (DataSourceConfigJson.TryFindProperty(root, "endpoint", out _, out var endpointNode))
        {
            if (DataSourceConfigJson.TryGetString(endpointNode, out var rawEndpoint) &&
                TryNormalizeEndpoint(rawEndpoint, out var normalizedEndpoint))
            {
                sanitized["endpoint"] = normalizedEndpoint;
            }
            else
            {
                hasSecret |= DataSourceConfigJson.HasMeaningfulContent(endpointNode);
            }
        }

        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, IsValidRegion, ref hasSecret, "region");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, IsValidBucket, ref hasSecret, "bucket");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, IsValidPrefix, ref hasSecret, "prefix");

        var format = DataSourceConfigJson.ReadOptionalStringStrict(root, "format")?.Trim().ToLowerInvariant();
        if (format is "parquet" or "jsonl")
        {
            sanitized["format"] = format;
        }

        if (DataSourceConfigJson.TryFindProperty(root, "pathStyle", out _, out var pathStyleNode))
        {
            if (DataSourceConfigJson.TryGetBoolean(pathStyleNode, out var pathStyle))
            {
                sanitized["pathStyle"] = pathStyle;
            }
            else
            {
                hasSecret |= DataSourceConfigJson.HasMeaningfulContent(pathStyleNode);
            }
        }
        else if (sanitized["provider"]?.GetValue<string>() is { } resolvedProvider)
        {
            sanitized["pathStyle"] = resolvedProvider == "minio";
        }

        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, SinkConfigContractHelpers.IsSafeIdentityText, ref hasSecret, "accessKey");
        SinkConfigContractHelpers.CopySanitizedString(
            root, sanitized, RelationalDataSourceConfigContract.IsValidTargetResourceGroup, ref hasSecret, "targetResourceGroup");
        SinkConfigContractHelpers.MarkSecretPresence(root, ref hasSecret, "secretKey");

        return new DataSourceProtectedConfig(sanitized.ToJsonString(), hasSecret);
    }

    internal static string NormalizeForWrite(JsonObject root)
    {
        var parsed = ParseForWrite(root, requireSecretKey: true);
        return parsed.ToJson(includeSecretKey: true);
    }

    internal static string MergeForWrite(JsonObject existingRoot, JsonObject submittedRoot)
    {
        var existing = ParseForWrite(existingRoot, requireSecretKey: false);
        var submitted = ParseForWrite(submittedRoot, requireSecretKey: false);

        if (existing.Scope != submitted.Scope && string.IsNullOrWhiteSpace(submitted.SecretKey))
        {
            throw SinkConfigContractHelpers.CredentialScopeChanged();
        }

        var effectiveSecretKey = !string.IsNullOrWhiteSpace(submitted.SecretKey)
            ? submitted.SecretKey
            : existing.SecretKey;

        if (string.IsNullOrWhiteSpace(effectiveSecretKey))
        {
            throw SinkConfigContractHelpers.InvalidConfig("对象存储配置必须提供 secretKey。");
        }

        return (submitted with { SecretKey = effectiveSecretKey }).ToJson(includeSecretKey: true);
    }

    internal static ObjectStorageSinkConfig ParseForWrite(string? configJson, bool requireSecretKey = false)
    {
        if (!DataSourceConfigJson.TryParseObject(configJson, out var root))
        {
            throw SinkConfigContractHelpers.InvalidConfig("对象存储配置必须是合法的 JSON 对象。");
        }

        return ParseForWrite(root, requireSecretKey);
    }

    internal static ObjectStorageSinkConfig ParseForWrite(JsonObject root, bool requireSecretKey)
    {
        var provider = DataSourceConfigJson.ReadOptionalStringStrict(root, "provider")?.Trim().ToLowerInvariant() switch
        {
            null or "" or "s3" or "s3-parquet" => "s3",
            "minio" => "minio",
            _ => throw SinkConfigContractHelpers.InvalidConfig("对象存储 provider 仅支持 s3 或 minio。"),
        };

        var rawEndpoint = DataSourceConfigJson.ReadOptionalStringStrict(root, "endpoint")?.Trim() ?? string.Empty;
        if (!TryNormalizeEndpoint(rawEndpoint, out var endpoint))
        {
            throw SinkConfigContractHelpers.InvalidConfig("对象存储 endpoint 必须是有效的 http/https 地址，且禁止携带用户信息、查询串或片段。");
        }

        var region = SinkConfigContractHelpers.ReadOptionalValidatedString(
            root, "us-east-1", IsValidRegion, "对象存储 region 格式无效。", "region").ToLowerInvariant();
        var bucket = SinkConfigContractHelpers.ReadRequiredString(
            root, "对象存储配置必须指定 bucket。", IsValidBucket, "对象存储 bucket 名称不符合 S3 命名规则。", "bucket").ToLowerInvariant();

        var rawPrefix = DataSourceConfigJson.ReadOptionalStringStrict(root, "prefix")?.Trim().Trim('/') ?? string.Empty;
        if (rawPrefix.Length > 0 && !IsValidPrefix(rawPrefix))
        {
            throw SinkConfigContractHelpers.InvalidConfig("对象存储 prefix 格式无效或包含越界路径段。");
        }

        var format = DataSourceConfigJson.ReadOptionalStringStrict(root, "format")?.Trim().ToLowerInvariant() switch
        {
            null or "" or "parquet" => "parquet",
            "jsonl" => "jsonl",
            _ => throw SinkConfigContractHelpers.InvalidConfig("对象存储 format 仅支持 parquet 或 jsonl。"),
        };

        var pathStyle = DataSourceConfigJson.ReadOptionalBoolStrict(root, "pathStyle") ?? (provider == "minio");
        var accessKey = SinkConfigContractHelpers.ReadRequiredString(
            root,
            "对象存储配置必须提供 accessKey。",
            SinkConfigContractHelpers.IsSafeIdentityText,
            "accessKey 包含非法控制字符。",
            "accessKey");

        var secretKey = DataSourceConfigJson.ReadOptionalStringStrict(root, "secretKey");
        if (requireSecretKey && string.IsNullOrWhiteSpace(secretKey))
        {
            throw SinkConfigContractHelpers.InvalidConfig("新建或重置对象存储配置时必须提供 secretKey。");
        }

        var targetResourceGroup = SinkConfigContractHelpers.ReadOptionalTargetResourceGroup(root);

        return new ObjectStorageSinkConfig(
            provider,
            endpoint,
            region,
            bucket,
            rawPrefix,
            format,
            pathStyle,
            accessKey,
            secretKey,
            targetResourceGroup);
    }

    internal static bool IsValidDestinationPath(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination) || destination.Length > 192)
        {
            return false;
        }

        if (destination.StartsWith('/') || destination.EndsWith('/') ||
            destination.Contains("..", StringComparison.Ordinal) ||
            destination.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        return DestinationPathRegex().IsMatch(destination);
    }

    private static bool TryNormalizeEndpoint(string rawEndpoint, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static bool IsValidRegion(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && RegionRegex().IsMatch(value);

    private static bool IsValidBucket(string value)
        => !string.IsNullOrWhiteSpace(value) && BucketRegex().IsMatch(value);

    private static bool IsValidPrefix(string value)
    {
        var trimmed = value.Trim('/');
        if (trimmed.Length == 0)
        {
            return true;
        }

        return trimmed.Length <= 128 &&
               !trimmed.Contains("..", StringComparison.Ordinal) &&
               !trimmed.Contains("//", StringComparison.Ordinal) &&
               DestinationPathRegex().IsMatch(trimmed);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex BucketRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RegionRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9/_.-]{0,191}$", RegexOptions.CultureInvariant)]
    private static partial Regex DestinationPathRegex();
}

internal readonly record struct ObjectStorageCredentialScope(
    string Provider,
    string Endpoint,
    string Region,
    string Bucket,
    bool PathStyle,
    string AccessKey);

internal readonly record struct ObjectStorageSinkConfig(
    string Provider,
    string Endpoint,
    string Region,
    string Bucket,
    string Prefix,
    string Format,
    bool PathStyle,
    string AccessKey,
    string? SecretKey,
    string TargetResourceGroup)
{
    internal ObjectStorageCredentialScope Scope => new(
        Provider,
        Endpoint,
        Region,
        Bucket,
        PathStyle,
        AccessKey);

    internal string BuildObjectKey(string destination, Guid runId)
    {
        var baseDir = BuildDestinationDirectory(destination);
        var extension = Format == "jsonl" ? "jsonl" : "parquet";
        return $"{baseDir}/run_{runId:N}.{extension}";
    }

    internal string BuildDestinationDirectory(string destination)
    {
        var cleanDest = destination.Trim().Trim('/');
        return string.IsNullOrEmpty(Prefix)
            ? cleanDest
            : $"{Prefix}/{cleanDest}";
    }

    internal string BuildManifestDirectory(string destination)
        => $"{BuildDestinationDirectory(destination)}/_swapmid/manifests";

    internal string BuildBatchManifestKey(string destination, string lsnToken, Guid runId)
        => $"{BuildManifestDirectory(destination)}/{lsnToken}_{runId:N}.json";

    internal string BuildLatestManifestKey(string destination)
        => $"{BuildDestinationDirectory(destination)}/_swapmid/latest.json";

    internal Uri BuildObjectUri(string objectKey)
    {
        var encodedKey = string.Join(
            '/',
            objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        var baseUri = new Uri(Endpoint, UriKind.Absolute);
        if (PathStyle)
        {
            return new Uri(baseUri, $"/{Uri.EscapeDataString(Bucket)}/{encodedKey}");
        }

        var builder = new UriBuilder(baseUri)
        {
            Host = $"{Bucket}.{baseUri.Host}",
            Path = $"/{encodedKey}",
        };
        return builder.Uri;
    }

    internal Uri BuildBucketProbeUri()
    {
        var baseUri = new Uri(Endpoint, UriKind.Absolute);
        if (PathStyle)
        {
            return new Uri(baseUri, $"/{Uri.EscapeDataString(Bucket)}?list-type=2&max-keys=0");
        }

        var builder = new UriBuilder(baseUri)
        {
            Host = $"{Bucket}.{baseUri.Host}",
            Path = "/",
            Query = "list-type=2&max-keys=0",
        };
        return builder.Uri;
    }

    internal string ToJson(bool includeSecretKey)
    {
        var json = new JsonObject
        {
            ["provider"] = Provider,
            ["endpoint"] = Endpoint,
            ["region"] = Region,
            ["bucket"] = Bucket,
            ["prefix"] = Prefix,
            ["format"] = Format,
            ["pathStyle"] = PathStyle,
            ["accessKey"] = AccessKey,
        };
        if (!string.IsNullOrWhiteSpace(TargetResourceGroup))
        {
            json["targetResourceGroup"] = TargetResourceGroup;
        }

        if (includeSecretKey && !string.IsNullOrWhiteSpace(SecretKey))
        {
            json["secretKey"] = SecretKey;
        }

        return json.ToJsonString();
    }
}
