using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwapMid.Infrastructure;
using SwapMid.Monitoring;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace SwapMid.DataIntegration;

/// <summary>
/// 将 ObjectStorage Lakehouse 表位置、协议与最新 Delta 版本同步到外部开放目录（Unity Catalog / Apache Iceberg REST）。
/// </summary>
public sealed partial class ExternalLakehouseCatalogSyncService : ITransientDependency
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ICdcStreamRepository _streamRepository;
    private readonly IRepository<DataSource, Guid> _sourceRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;
    private readonly ILogger<ExternalLakehouseCatalogSyncService> _logger;

    public ExternalLakehouseCatalogSyncService(
        ICdcStreamRepository streamRepository,
        IRepository<DataSource, Guid> sourceRepository,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IUnitOfWorkManager unitOfWorkManager,
        PipelineExecutionFailurePolicy failurePolicy,
        ILogger<ExternalLakehouseCatalogSyncService> logger)
    {
        _streamRepository = streamRepository;
        _sourceRepository = sourceRepository;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _unitOfWorkManager = unitOfWorkManager;
        _failurePolicy = failurePolicy;
        _logger = logger;
    }

    public async Task<CdcCatalogSyncResultDto> SyncAsync(
        Guid streamId,
        CancellationToken cancellationToken = default)
    {
        var stream = await _streamRepository.GetAsync(streamId, cancellationToken: cancellationToken);
        if (stream.CatalogType == LakehouseCatalogType.None || !stream.CatalogSinkId.HasValue)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "当前 CDC 流未配置外部开放目录（Unity Catalog / Iceberg REST）同步。");
        }

        if (stream.TargetMode != CdcTargetMode.ExternalSink ||
            !stream.TargetSinkId.HasValue ||
            string.IsNullOrWhiteSpace(stream.TargetSinkDestination))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", "仅已绑定 ObjectStorage 接收端的 CDC 流支持外部目录元数据同步。");
        }

        var targetSink = await _sourceRepository.GetAsync(stream.TargetSinkId.Value, cancellationToken: cancellationToken);
        var catalogSink = await _sourceRepository.GetAsync(stream.CatalogSinkId.Value, cancellationToken: cancellationToken);

        return await ExecuteAndPersistSyncAsync(
            stream,
            targetSink,
            catalogSink,
            versionOverride: null,
            throwOnFailure: true,
            cancellationToken);
    }

    public async Task<CdcCatalogSyncResultDto?> TryBestEffortSyncAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        long? versionOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (stream.CatalogType == LakehouseCatalogType.None ||
            !stream.CatalogSinkId.HasValue ||
            targetSink.Type != DataSourceType.ObjectStorage)
        {
            return null;
        }

        var catalogSink = await _sourceRepository.FindAsync(
            stream.CatalogSinkId.Value,
            cancellationToken: cancellationToken);
        if (catalogSink == null)
        {
            var label = ToMetricLabel(stream.CatalogType);
            PlatformMetrics.CdcCatalogSyncsTotal.WithLabels(label, "skipped").Inc();
            return null;
        }

        return await ExecuteAndPersistSyncAsync(
            stream,
            targetSink,
            catalogSink,
            versionOverride,
            throwOnFailure: false,
            cancellationToken);
    }

    private async Task<CdcCatalogSyncResultDto> ExecuteAndPersistSyncAsync(
        CdcStreamDefinition stream,
        DataSource targetSink,
        DataSource catalogSink,
        long? versionOverride,
        bool throwOnFailure,
        CancellationToken cancellationToken)
    {
        var label = ToMetricLabel(stream.CatalogType);
        var nowUtc = DateTime.UtcNow;

        try
        {
            if (targetSink.Type != DataSourceType.ObjectStorage ||
                string.IsNullOrWhiteSpace(stream.TargetSinkDestination))
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "Lakehouse 目录同步要求目标接收端为 ObjectStorage。");
            }

            if (catalogSink.Type != DataSourceType.RestApi)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                    .WithData("Reason", "外部 Catalog 数据源必须为 RestApi 类型。");
            }

            var storageCfg = ObjectStorageSinkConfigContract.ParseForWrite(targetSink.Config, requireSecretKey: false);
            var destination = stream.TargetSinkDestination.Trim();
            var tableDir = storageCfg.BuildDestinationDirectory(destination).TrimEnd('/');
            var locationUri = string.IsNullOrEmpty(tableDir)
                ? $"s3://{storageCfg.Bucket}"
                : $"s3://{storageCfg.Bucket}/{tableDir}";

            var deltaVersion = versionOverride;
            long? icebergSnapshotId = null;
            string? icebergMetadataUri = null;
            if (stream.LakehouseTableFormat is LakehouseTableFormat.DeltaLake or LakehouseTableFormat.ApacheIceberg)
            {
                var latestJson = await CdcObjectStorageLakehouseWriter.TryGetObjectStorageJsonAsync(
                    _httpClientFactory,
                    _configuration,
                    storageCfg,
                    storageCfg.BuildLatestManifestKey(destination),
                    cancellationToken);
                var latestMeta = TryReadLatestLakehouseMeta(latestJson);
                if (!deltaVersion.HasValue)
                {
                    deltaVersion = stream.LakehouseTableFormat == LakehouseTableFormat.ApacheIceberg
                        ? latestMeta.IcebergSequenceNumber
                        : latestMeta.DeltaVersion;
                }

                if (stream.LakehouseTableFormat == LakehouseTableFormat.ApacheIceberg)
                {
                    icebergSnapshotId = latestMeta.IcebergSnapshotId;
                    if (!string.IsNullOrWhiteSpace(latestMeta.IcebergMetadataLocation))
                    {
                        icebergMetadataUri = $"s3://{storageCfg.Bucket}/{latestMeta.IcebergMetadataLocation.TrimStart('/')}";
                    }
                }
            }

            var restCfg = ParseCatalogRestConfig(catalogSink.Config);
            await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
                restCfg.BaseUri,
                _configuration,
                cancellationToken);

            var columns = ResolveCatalogColumns(stream);
            var fullIdentifier = stream.CatalogType == LakehouseCatalogType.UnityCatalogRest
                ? await SyncUnityCatalogAsync(restCfg, stream, locationUri, deltaVersion, icebergSnapshotId, icebergMetadataUri, columns, cancellationToken)
                : await SyncIcebergRestAsync(restCfg, stream, locationUri, deltaVersion, icebergSnapshotId, icebergMetadataUri, columns, cancellationToken);

            var versionLabel = deltaVersion.HasValue ? $"v{deltaVersion.Value}" : "manifest";
            var okMessage = $"已同步表 {fullIdentifier}（{versionLabel}）至 {(stream.CatalogType == LakehouseCatalogType.UnityCatalogRest ? "Unity Catalog" : "Apache Iceberg REST")}。";

            await PersistAttemptAsync(stream.Id, succeeded: true, deltaVersion, nowUtc, okMessage, cancellationToken);
            PlatformMetrics.CdcCatalogSyncsTotal.WithLabels(label, "succeeded").Inc();

            return new CdcCatalogSyncResultDto
            {
                StreamId = stream.Id,
                CatalogType = stream.CatalogType,
                Synced = true,
                SyncedVersion = deltaVersion,
                CatalogTableIdentifier = fullIdentifier,
                LocationUri = locationUri,
                SyncedAtUtc = nowUtc,
                Message = okMessage,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = _failurePolicy.DescribeFailure(ex, stream.Id);
            var failStatus = $"同步失败（{failure.ErrorCode} / {failure.ExceptionType}）";
            await PersistAttemptAsync(stream.Id, succeeded: false, syncedVersion: null, nowUtc, failStatus, cancellationToken);
            PlatformMetrics.CdcCatalogSyncsTotal.WithLabels(label, "failed").Inc();
            _logger.LogWarning(
                "CDC Lakehouse 外部目录同步失败 streamId={StreamId} catalogType={CatalogType} code={Code} exceptionType={Type}",
                stream.Id,
                stream.CatalogType,
                failure.ErrorCode,
                failure.ExceptionType);

            if (throwOnFailure)
            {
                throw;
            }

            return new CdcCatalogSyncResultDto
            {
                StreamId = stream.Id,
                CatalogType = stream.CatalogType,
                Synced = false,
                SyncedVersion = stream.CatalogLastSyncedVersion,
                CatalogTableIdentifier = BuildDisplayIdentifier(stream),
                SyncedAtUtc = nowUtc,
                Message = failStatus,
            };
        }
    }

    private async Task<string> SyncUnityCatalogAsync(
        CatalogRestConfig restCfg,
        CdcStreamDefinition stream,
        string locationUri,
        long? deltaVersion,
        long? icebergSnapshotId,
        string? icebergMetadataUri,
        IReadOnlyList<CatalogColumnDescriptor> columns,
        CancellationToken cancellationToken)
    {
        var (catalogName, schemaName, tableName) = ResolveUnityThreeLevelName(restCfg, stream);
        var fullName = $"{catalogName}.{schemaName}.{tableName}";
        var format = stream.LakehouseTableFormat switch
        {
            LakehouseTableFormat.DeltaLake => "DELTA",
            LakehouseTableFormat.ApacheIceberg => "ICEBERG",
            _ => "PARQUET",
        };

        var properties = BuildMetadataProperties(stream, locationUri, deltaVersion, icebergSnapshotId, icebergMetadataUri);
        var createBody = new
        {
            name = tableName,
            catalog_name = catalogName,
            schema_name = schemaName,
            table_type = "EXTERNAL",
            data_source_format = format,
            storage_location = locationUri,
            comment = stream.Description ?? $"Managed by SwapMid CDC stream {stream.Name}",
            columns = columns.Select((c, idx) => new
            {
                name = c.Name,
                type_text = c.SparkType,
                type_name = c.SparkType.ToUpperInvariant(),
                type_json = JsonSerializer.Serialize(new { name = c.Name, type = c.SparkType, nullable = true }),
                position = idx,
                nullable = true,
            }).ToArray(),
            properties,
        };

        var createUri = BuildEndpointUri(restCfg.BaseUri, "/api/2.1/unity-catalog/tables");
        var createResp = await SendJsonAsync(HttpMethod.Post, createUri, createBody, restCfg, cancellationToken);
        if (createResp.StatusCode == HttpStatusCode.Conflict)
        {
            var updateUri = BuildEndpointUri(restCfg.BaseUri, $"/api/2.1/unity-catalog/tables/{Uri.EscapeDataString(fullName)}");
            var updateBody = new
            {
                storage_location = locationUri,
                properties,
            };
            var patchResp = await SendJsonAsync(HttpMethod.Patch, updateUri, updateBody, restCfg, cancellationToken);
            if (patchResp.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
            {
                patchResp = await SendJsonAsync(HttpMethod.Put, updateUri, createBody, restCfg, cancellationToken);
            }

            EnsureHttpSuccess(patchResp, "Unity Catalog 表元数据更新");
            return fullName;
        }

        EnsureHttpSuccess(createResp, "Unity Catalog 表注册");
        return fullName;
    }

    private async Task<string> SyncIcebergRestAsync(
        CatalogRestConfig restCfg,
        CdcStreamDefinition stream,
        string locationUri,
        long? deltaVersion,
        long? icebergSnapshotId,
        string? icebergMetadataUri,
        IReadOnlyList<CatalogColumnDescriptor> columns,
        CancellationToken cancellationToken)
    {
        var ns = string.IsNullOrWhiteSpace(stream.CatalogNamespace) ? "default" : stream.CatalogNamespace.Trim();
        var table = string.IsNullOrWhiteSpace(stream.CatalogTableName) ? stream.Name : stream.CatalogTableName.Trim();
        var prefixSeg = string.IsNullOrWhiteSpace(restCfg.Prefix)
            ? string.Empty
            : $"/{Uri.EscapeDataString(restCfg.Prefix.Trim('/'))}";
        var escapedNs = Uri.EscapeDataString(ns);

        var nsUri = BuildEndpointUri(restCfg.BaseUri, $"/v1{prefixSeg}/namespaces");
        var nsResp = await SendJsonAsync(
            HttpMethod.Post,
            nsUri,
            new { @namespace = ns.Split('.', StringSplitOptions.RemoveEmptyEntries) },
            restCfg,
            cancellationToken);
        if (!nsResp.IsSuccess &&
            nsResp.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound))
        {
            EnsureHttpSuccess(nsResp, "Iceberg REST 命名空间初始化");
        }

        var properties = BuildMetadataProperties(stream, locationUri, deltaVersion, icebergSnapshotId, icebergMetadataUri);
        var createUri = BuildEndpointUri(restCfg.BaseUri, $"/v1{prefixSeg}/namespaces/{escapedNs}/tables");
        var createBody = new
        {
            name = table,
            location = locationUri,
            schema = new Dictionary<string, object>
            {
                ["type"] = "struct",
                ["schema-id"] = 0,
                ["fields"] = columns.Select((c, idx) => new
                {
                    id = idx + 1,
                    name = c.Name,
                    required = false,
                    type = c.IcebergType,
                }).ToArray(),
            },
            properties,
        };

        var createResp = await SendJsonAsync(HttpMethod.Post, createUri, createBody, restCfg, cancellationToken);
        if (createResp.StatusCode == HttpStatusCode.Conflict)
        {
            var commitUri = BuildEndpointUri(
                restCfg.BaseUri,
                $"/v1{prefixSeg}/namespaces/{escapedNs}/tables/{Uri.EscapeDataString(table)}");
            var commitBody = new
            {
                requirements = Array.Empty<object>(),
                updates = new object[]
                {
                    new
                    {
                        action = "set-properties",
                        updates = properties,
                    },
                },
            };
            var updateResp = await SendJsonAsync(HttpMethod.Post, commitUri, commitBody, restCfg, cancellationToken);
            EnsureHttpSuccess(updateResp, "Apache Iceberg REST 表属性更新");
            return $"{ns}.{table}";
        }

        EnsureHttpSuccess(createResp, "Apache Iceberg REST 表注册");
        return $"{ns}.{table}";
    }

    private async Task<RestCallResult> SendJsonAsync(
        HttpMethod method,
        Uri targetUri,
        object payload,
        CatalogRestConfig restCfg,
        CancellationToken cancellationToken)
    {
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            targetUri,
            _configuration,
            cancellationToken);

        var bodyJson = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(method, targetUri)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(restCfg.BearerToken))
        {
            if (string.IsNullOrWhiteSpace(restCfg.ApiKeyHeader) ||
                string.Equals(restCfg.ApiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", restCfg.BearerToken);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(restCfg.ApiKeyHeader, restCfg.BearerToken);
            }
        }

        foreach (var kv in restCfg.ExtraHeaders)
        {
            if (string.Equals(kv.Key, "Host", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kv.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var snippet = await response.Content.ReadAsStringAsync(cancellationToken);
        return new RestCallResult(response.IsSuccessStatusCode, response.StatusCode, snippet);
    }

    private async Task PersistAttemptAsync(
        Guid streamId,
        bool succeeded,
        long? syncedVersion,
        DateTime attemptAtUtc,
        string status,
        CancellationToken cancellationToken)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var latest = await _streamRepository.FindAsync(streamId, cancellationToken: cancellationToken);
        if (latest != null)
        {
            latest.RecordCatalogSyncAttempt(succeeded, syncedVersion, attemptAtUtc, status);
            await _streamRepository.UpdateAsync(latest, autoSave: true, cancellationToken: cancellationToken);
        }

        await uow.CompleteAsync(cancellationToken);
    }
}
