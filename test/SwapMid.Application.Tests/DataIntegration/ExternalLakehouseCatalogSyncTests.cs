using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SwapMid.Localization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

public class ExternalLakehouseCatalogSyncTests
{
    [Fact]
    public async Task UnityCatalogRest_Should_Register_Then_Update_On_409_Conflict()
    {
        var sinkId = Guid.NewGuid();
        var catalogSinkId = Guid.NewGuid();
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-uc-delta",
            Guid.NewGuid(),
            "mysql_binlog",
            "binlog_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: sinkId,
            targetSinkDestination: "orders/delta",
            sourceType: CdcSourceType.MySqlBinlog,
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake,
            catalogType: LakehouseCatalogType.UnityCatalogRest,
            catalogSinkId: catalogSinkId,
            catalogNamespace: "analytics.swapmid_cdc",
            catalogTableName: "orders");

        var objectSink = new DataSource(
            sinkId,
            "minio-lake",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"http://198.51.100.10:9000","region":"us-east-1","bucket":"swapmid-lake","prefix":"cdc","format":"parquet","pathStyle":true,"accessKey":"AK","secretKey":"SK"}""",
            null);
        var ucSink = new DataSource(
            catalogSinkId,
            "unity-catalog-oss",
            DataSourceType.RestApi,
            """{"baseUrl":"http://198.51.100.20:8080","bearerToken":"uc-secret-token-123"}""",
            null);

        var s3AndUcHandler = new MockCatalogHttpHandler(new Dictionary<string, string>
        {
            ["/swapmid-lake/cdc/orders/delta/_swapmid/latest.json"] = "{\"latestDeltaVersion\":4}",
        });

        var service = CreateService(stream, [objectSink, ucSink], s3AndUcHandler, allowedHost: "198.51.100.10,198.51.100.20");

        // First sync: POST create succeeds -> 201 Created
        var first = await service.SyncAsync(stream.Id);
        first.Synced.ShouldBeTrue();
        first.CatalogType.ShouldBe(LakehouseCatalogType.UnityCatalogRest);
        first.CatalogTableIdentifier.ShouldBe("analytics.swapmid_cdc.orders");
        first.SyncedVersion.ShouldBe(4);
        first.LocationUri.ShouldBe("s3://swapmid-lake/cdc/orders/delta");
        stream.CatalogLastSyncedVersion.ShouldBe(4);
        s3AndUcHandler.RecordedRequests.ShouldContain(r =>
            r.Method == "POST" &&
            r.Path == "/api/2.1/unity-catalog/tables" &&
            r.AuthHeader == "Bearer uc-secret-token-123" &&
            r.Body.Contains("\"data_source_format\":\"DELTA\"", StringComparison.Ordinal));

        // Second sync when table already exists (POST -> 409): upgrades idempotently via PATCH
        s3AndUcHandler.Store["/swapmid-lake/cdc/orders/delta/_swapmid/latest.json"] = "{\"latestDeltaVersion\":7}";
        var second = await service.SyncAsync(stream.Id);
        second.Synced.ShouldBeTrue();
        second.SyncedVersion.ShouldBe(7);
        s3AndUcHandler.RecordedRequests.ShouldContain(r =>
            r.Method == "PATCH" &&
            r.Path == "/api/2.1/unity-catalog/tables/analytics.swapmid_cdc.orders" &&
            r.Body.Contains("\"swapmid.delta_version\":\"7\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IcebergRest_Should_Ensure_Namespace_And_Commit_Property_Updates()
    {
        var sinkId = Guid.NewGuid();
        var catalogSinkId = Guid.NewGuid();
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-iceberg",
            Guid.NewGuid(),
            "mysql_binlog",
            "binlog_orders",
            CdcTargetMode.ExternalSink,
            targetSinkId: sinkId,
            targetSinkDestination: "orders/delta",
            sourceType: CdcSourceType.MySqlBinlog,
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake,
            catalogType: LakehouseCatalogType.IcebergRest,
            catalogSinkId: catalogSinkId,
            catalogNamespace: "swapmid_cdc",
            catalogTableName: "orders");

        var objectSink = new DataSource(
            sinkId,
            "minio-lake",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"http://198.51.100.10:9000","region":"us-east-1","bucket":"swapmid-lake","prefix":"cdc","format":"parquet","pathStyle":true,"accessKey":"AK","secretKey":"SK"}""",
            null);
        var icebergSink = new DataSource(
            catalogSinkId,
            "iceberg-rest",
            DataSourceType.RestApi,
            """{"baseUrl":"http://198.51.100.30:8181","apiKey":"iceberg-key-abc","apiKeyHeader":"X-Iceberg-Token"}""",
            null);

        var handler = new MockCatalogHttpHandler(new Dictionary<string, string>
        {
            ["/swapmid-lake/cdc/orders/delta/_swapmid/latest.json"] = "{\"latestDeltaVersion\":2}",
        });
        var service = CreateService(stream, [objectSink, icebergSink], handler, allowedHost: "198.51.100.10,198.51.100.30");

        var first = await service.SyncAsync(stream.Id);
        first.Synced.ShouldBeTrue();
        first.CatalogTableIdentifier.ShouldBe("swapmid_cdc.orders");
        handler.RecordedRequests.ShouldContain(r => r.Method == "POST" && r.Path == "/v1/namespaces");
        handler.RecordedRequests.ShouldContain(r =>
            r.Method == "POST" &&
            r.Path == "/v1/namespaces/swapmid_cdc/tables" &&
            r.CustomHeader == "iceberg-key-abc" &&
            r.Body.Contains("\"location\":\"s3://swapmid-lake/cdc/orders/delta\"", StringComparison.Ordinal));

        handler.Store["/swapmid-lake/cdc/orders/delta/_swapmid/latest.json"] = "{\"latestDeltaVersion\":5}";
        var second = await service.SyncAsync(stream.Id);
        second.SyncedVersion.ShouldBe(5);
        handler.RecordedRequests.ShouldContain(r =>
            r.Method == "POST" &&
            r.Path == "/v1/namespaces/swapmid_cdc/tables/orders" &&
            r.Body.Contains("\"action\":\"set-properties\"", StringComparison.Ordinal) &&
            r.Body.Contains("\"swapmid.delta_version\":\"5\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncAsync_Should_Block_Ssrf_Metadata_Endpoint_Without_Dispatching_Http()
    {
        var sinkId = Guid.NewGuid();
        var catalogSinkId = Guid.NewGuid();
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-ssrf",
            Guid.NewGuid(),
            "swapmid_pub",
            "swapmid_slot",
            CdcTargetMode.ExternalSink,
            targetSinkId: sinkId,
            targetSinkDestination: "orders/delta",
            catalogType: LakehouseCatalogType.UnityCatalogRest,
            catalogSinkId: catalogSinkId,
            catalogNamespace: "analytics",
            catalogTableName: "orders");

        var objectSink = new DataSource(
            sinkId,
            "minio-lake",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"http://198.51.100.10:9000","region":"us-east-1","bucket":"swapmid-lake","prefix":"cdc","format":"parquet","pathStyle":true,"accessKey":"AK","secretKey":"SK"}""",
            null);
        var ssrfSink = new DataSource(
            catalogSinkId,
            "bad-catalog",
            DataSourceType.RestApi,
            """{"baseUrl":"http://169.254.169.254/latest/meta-data"}""",
            null);

        var handler = new MockCatalogHttpHandler(new Dictionary<string, string>());
        var service = CreateService(stream, [objectSink, ssrfSink], handler, allowedHost: "198.51.100.10");

        await Should.ThrowAsync<BusinessException>(() => service.SyncAsync(stream.Id));
        handler.RecordedRequests.Count.ShouldBe(0);
    }

    private static ExternalLakehouseCatalogSyncService CreateService(
        CdcStreamDefinition stream,
        IReadOnlyList<DataSource> dataSources,
        MockCatalogHttpHandler handler,
        string allowedHost)
    {
        var streamRepo = Substitute.For<ICdcStreamRepository>();
        streamRepo.GetAsync(stream.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(stream);
        streamRepo.FindAsync(stream.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(stream);
        streamRepo.UpdateAsync(Arg.Any<CdcStreamDefinition>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<CdcStreamDefinition>());

        var dsRepo = Substitute.For<IRepository<DataSource, Guid>>();
        foreach (var ds in dataSources)
        {
            dsRepo.GetAsync(ds.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ds);
            dsRepo.FindAsync(ds.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(ds);
        }

        var uow = Substitute.For<IUnitOfWork>();
        var uowManager = Substitute.For<IUnitOfWorkManager>();
        uowManager.Begin(Arg.Any<AbpUnitOfWorkOptions>(), Arg.Any<bool>()).Returns(uow);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = allowedHost,
            })
            .Build();

        var localizer = Substitute.For<IStringLocalizer<SwapMidResource>>();
        localizer[Arg.Any<string>()].Returns(ci => new LocalizedString(ci.Arg<string>(), ci.Arg<string>()));
        localizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(ci => new LocalizedString(ci.Arg<string>(), ci.Arg<string>()));

        return new ExternalLakehouseCatalogSyncService(
            streamRepo,
            dsRepo,
            factory,
            config,
            uowManager,
            new PipelineExecutionFailurePolicy(
                Substitute.For<ICorrelationIdProvider>(),
                localizer),
            NullLogger<ExternalLakehouseCatalogSyncService>.Instance);
    }

    private sealed record RecordedHttpRequest(
        string Method,
        string Path,
        string? AuthHeader,
        string? CustomHeader,
        string Body);

    private sealed class MockCatalogHttpHandler : HttpMessageHandler
    {
        private readonly HashSet<string> _createdTables = new(StringComparer.Ordinal);
        public Dictionary<string, string> Store { get; }
        public List<RecordedHttpRequest> RecordedRequests { get; } = [];

        public MockCatalogHttpHandler(Dictionary<string, string> store) => Store = store;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content != null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
            var auth = request.Headers.Authorization?.ToString();
            request.Headers.TryGetValues("X-Iceberg-Token", out var customVals);
            var custom = customVals is null ? null : string.Join(",", customVals);

            RecordedRequests.Add(new RecordedHttpRequest(request.Method.Method, path, auth, custom, body));

            if (request.Method == HttpMethod.Get)
            {
                return Store.TryGetValue(path, out var text)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Post && path == "/api/2.1/unity-catalog/tables")
            {
                using var doc = JsonDocument.Parse(body);
                var fullName = $"{doc.RootElement.GetProperty("catalog_name").GetString()}.{doc.RootElement.GetProperty("schema_name").GetString()}.{doc.RootElement.GetProperty("name").GetString()}";
                if (!_createdTables.Add(fullName))
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = new StringContent("{\"error_code\":\"TABLE_ALREADY_EXISTS\"}", Encoding.UTF8, "application/json"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            }

            if (request.Method == HttpMethod.Patch && path.StartsWith("/api/2.1/unity-catalog/tables/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
            }

            if (request.Method == HttpMethod.Post && path == "/v1/namespaces")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/tables", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(body);
                var key = $"{path}/{doc.RootElement.GetProperty("name").GetString()}";
                if (!_createdTables.Add(key))
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }

            if (request.Method == HttpMethod.Post && path.Contains("/tables/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
