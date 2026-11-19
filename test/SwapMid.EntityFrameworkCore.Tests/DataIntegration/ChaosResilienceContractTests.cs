using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SwapMid.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

[Collection(EntityFrameworkCoreTestCollections.Platform)]
public sealed class ChaosResilienceContractTests : SwapMidEntityFrameworkCoreTestBase
{
    private readonly IDataSourceAppService _dataSourceAppService;
    private readonly IDataPipelineAppService _dataPipelineAppService;
    private readonly IPipelineExecutionAppService _executionAppService;
    private readonly ICdcStreamAppService _cdcAppService;
    private readonly ICdcStreamRepository _cdcStreamRepository;
    private readonly IRepository<DataSource, Guid> _dataSourceRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly PipelineExecutionFailurePolicy _failurePolicy;
    private readonly FakeEtlPipelineRunner _etlRunner;
    private readonly FakeEtlTargetReceiptStore _receiptStore;

    public ChaosResilienceContractTests()
    {
        _dataSourceAppService = GetRequiredService<IDataSourceAppService>();
        _dataPipelineAppService = GetRequiredService<IDataPipelineAppService>();
        _executionAppService = GetRequiredService<IPipelineExecutionAppService>();
        _cdcAppService = GetRequiredService<ICdcStreamAppService>();
        _cdcStreamRepository = GetRequiredService<ICdcStreamRepository>();
        _dataSourceRepository = GetRequiredService<IRepository<DataSource, Guid>>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        _failurePolicy = GetRequiredService<PipelineExecutionFailurePolicy>();
        _etlRunner = (FakeEtlPipelineRunner)GetRequiredService<IEtlPipelineRunner>();
        _receiptStore = GetRequiredService<FakeEtlTargetReceiptStore>();
        _etlRunner.Reset();
        _receiptStore.Reset();
    }

    [Fact]
    public async Task Scenario_1_MidFlight_Crash_OutcomeUnknown_Should_Block_Quarantine_And_Converge_Zero_Duplicates_Via_Receipt()
    {
        var source = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"Chaos Src {Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RelationalDatabase,
            Config = JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                host = "localhost",
                port = 1433,
                database = "SwapMid",
                username = "reader",
                targetResourceGroup = $"chaos-src-{Guid.NewGuid():N}",
            }),
        });
        var target = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"Chaos Tgt {Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RelationalDatabase,
            Config = JsonSerializer.Serialize(new
            {
                provider = "sqlserver",
                host = "localhost",
                port = 1433,
                database = "SwapMid",
                username = "writer",
                targetResourceGroup = $"chaos-tgt-{Guid.NewGuid():N}",
            }),
        });

        var pipeline = await _dataPipelineAppService.CreateAsync(new DataPipelineCreateUpdateDto
        {
            Name = $"Chaos ETL {Guid.NewGuid():N}"[..32],
            SourceId = source.Id,
            TargetId = target.Id,
            TransformConfig = """{"sourceQuery":"SELECT 1 AS Id","targetTable":"dbo.OrdersChaos","mode":"append-receipt-v1"}""",
        });
        await _dataPipelineAppService.ActivateAsync(pipeline.Id);

        // Inject mid-commit transport crash (OutcomeUnknown)
        var crashedRunId = Guid.NewGuid();
        _etlRunner.ExceptionToThrow = new EtlTargetCommitOutcomeUnknownException();
        var crashed = await _dataPipelineAppService.ExecuteAsync(pipeline.Id, crashedRunId);
        crashed.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);

        // Same RunId replay converges idempotently without re-running ETL writer
        var replay = await _dataPipelineAppService.ExecuteAsync(pipeline.Id, crashedRunId);
        replay.Id.ShouldBe(crashedRunId);
        replay.Status.ShouldBe(ExecutionStatus.OutcomeUnknown);
        _etlRunner.RunCalls.ShouldBe(1);

        // New RunId is strictly quarantined until receipt reconciliation resolves crash
        var blocked = await Should.ThrowAsync<BusinessException>(() =>
            _dataPipelineAppService.ExecuteAsync(pipeline.Id, Guid.NewGuid()));
        blocked.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionOutcomeUnresolved);
        _etlRunner.RunCalls.ShouldBe(1);

        // Target Receipt v1 machine probe verifies write landed -> converges cleanly & unblocks target
        _receiptStore.Behavior = FakeReceiptBehavior.Match;
        _etlRunner.ExceptionToThrow = null;
        var verified = await _executionAppService.VerifyTargetReceiptAsync(crashedRunId);
        verified.Status.ShouldBe(ExecutionStatus.Succeeded);
        verified.ProcessedRows.ShouldBe(FakeEtlTargetReceiptStore.DefaultProcessedRows);
        verified.ReceiptVerificationSource.ShouldBe("target-receipt-v1");

        var nextRun = await _dataPipelineAppService.ExecuteAsync(pipeline.Id, Guid.NewGuid());
        nextRun.Status.ShouldBe(ExecutionStatus.Succeeded);
        _etlRunner.RunCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Scenario_2_DeltaLake_Cas_412_Race_And_Same_RunId_Replay_Should_Converge_Idempotently()
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "chaos-delta-cas",
            Guid.NewGuid(),
            "mysql_binlog",
            "binlog_chaos",
            CdcTargetMode.ExternalSink,
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "orders/chaos",
            sourceType: CdcSourceType.MySqlBinlog,
            lakehouseTableFormat: LakehouseTableFormat.DeltaLake);

        var run0 = Guid.NewGuid();
        store["/lake/cdc/orders/chaos/_delta_log/00000000000000000000.json"] =
            DeltaLakeCommitWriter.BuildAppendNdjson(
                0, stream, run0, $"part_{run0:N}.parquet", 512, 1730000000000L, "{\"numRecords\":5}", isInitialSnapshot: true);

        var s3Handler = new ChaosS3AndCatalogHandler(store, forceConflictOnVersion1Once: true);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(s3Handler, disposeHandler: false));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.77",
            })
            .Build();

        var s3Cfg = new ObjectStorageSinkConfig(
            "minio", "http://198.51.100.77:9000", "us-east-1", "lake", "cdc", "parquet", true, "ak", "sk", "");
        var latestDoc = new CdcObjectStorageLakehouseWriter.CdcLatestCheckpointDocument
        {
            StreamId = stream.Id,
            StreamName = stream.Name,
            Destination = "orders/chaos",
            LatestDeltaVersion = 0,
        };

        // Run A hits 412 race on v1 (pre-claimed by concurrent writer) -> converges onto v2
        var runA = Guid.NewGuid();
        var committedVersion = await DeltaLakeCommitWriter.CommitAppendAsync(
            factory, config, s3Cfg, "orders/chaos", stream, latestDoc,
            runA, $"cdc/orders/chaos/part_{runA:N}.parquet", 1024, 12, false, DateTime.UtcNow, CancellationToken.None);
        committedVersion.ShouldBe(2);

        // Crash & replay of Run A with stale LatestDeltaVersion=0 still discovers its own runId at v2 (no duplicate v3!)
        latestDoc.LatestDeltaVersion = 0;
        var replayedVersion = await DeltaLakeCommitWriter.CommitAppendAsync(
            factory, config, s3Cfg, "orders/chaos", stream, latestDoc,
            runA, $"cdc/orders/chaos/part_{runA:N}.parquet", 1024, 12, false, DateTime.UtcNow, CancellationToken.None);
        replayedVersion.ShouldBe(2);
        store.ContainsKey("/lake/cdc/orders/chaos/_delta_log/00000000000000000003.json").ShouldBeFalse();
    }

    [Fact]
    public async Task Scenario_3_External_Catalog_503_Outage_Should_Not_Block_Cdc_Checkpoint_Lsn_And_Recover_On_Retry()
    {
        var pgSrc = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"chaos-pg-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RelationalDatabase,
            Config = """{"provider":"postgresql","host":"127.0.0.1","port":5432,"database":"swapmid","username":"postgres"}""",
        });
        var minioSink = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"chaos-minio-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.ObjectStorage,
            Config = """{"provider":"minio","endpoint":"http://198.51.100.77:9000","region":"us-east-1","bucket":"lake","prefix":"cdc","format":"parquet","pathStyle":true,"accessKey":"ak","secretKey":"sk"}""",
        });
        var catalogSink = await _dataSourceAppService.CreateAsync(new DataSourceCreateUpdateDto
        {
            Name = $"chaos-uc-{Guid.NewGuid():N}"[..32],
            Type = DataSourceType.RestApi,
            Config = """{"baseUrl":"http://198.51.100.88:8080","bearerToken":"super-secret-catalog-token"}""",
        });

        var created = await _cdcAppService.CreateAsync(new CdcStreamCreateUpdateDto
        {
            Name = $"chaos-cdc-outage-{Guid.NewGuid():N}"[..32],
            SourceId = pgSrc.Id,
            PublicationName = "chaos_pub",
            SlotName = "chaos_slot",
            MonitoredTables = "public.orders",
            TargetMode = CdcTargetMode.ExternalSink,
            TargetSinkId = minioSink.Id,
            TargetSinkDestination = "orders/outage",
            LakehouseTableFormat = LakehouseTableFormat.DeltaLake,
            CatalogType = LakehouseCatalogType.UnityCatalogRest,
            CatalogSinkId = catalogSink.Id,
            CatalogNamespace = "analytics.swapmid",
            CatalogTableName = "orders",
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await _cdcStreamRepository.GetAsync(created.Id);
            entity.Activate();
            entity.MarkCaptureSucceeded("0/2A00B10", 25, DateTime.UtcNow);
            await _cdcStreamRepository.UpdateAsync(entity, autoSave: true);
        });

        var store = new Dictionary<string, string>
        {
            ["/lake/cdc/orders/outage/_swapmid/latest.json"] = "{\"latestDeltaVersion\":3}",
        };
        var handler = new ChaosS3AndCatalogHandler(store, forceConflictOnVersion1Once: false)
        {
            CatalogOutage503 = true,
        };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.77,198.51.100.88",
            })
            .Build();

        var syncService = new ExternalLakehouseCatalogSyncService(
            _cdcStreamRepository,
            _dataSourceRepository,
            factory,
            config,
            _unitOfWorkManager,
            _failurePolicy,
            NullLogger<ExternalLakehouseCatalogSyncService>.Instance);

        var loadedStream = await WithUnitOfWorkAsync(() => _cdcStreamRepository.GetAsync(created.Id));
        var loadedSink = await WithUnitOfWorkAsync(() => _dataSourceRepository.GetAsync(minioSink.Id));

        // Best-effort sync during 503 outage must NOT throw or touch CDC CheckpointLsn / Active status
        var failedAttempt = await syncService.TryBestEffortSyncAsync(loadedStream, loadedSink);
        failedAttempt.ShouldNotBeNull();
        failedAttempt.Synced.ShouldBeFalse();

        var afterOutage = await _cdcAppService.GetAsync(created.Id);
        afterOutage.Status.ShouldBe(CdcStreamStatus.Active);
        afterOutage.CheckpointLsn.ShouldBe("0/2A00B10");
        afterOutage.TotalCapturedCount.ShouldBe(25);
        afterOutage.CatalogLastSyncedVersion.ShouldBeNull();
        afterOutage.CatalogLastSyncStatus.ShouldNotBeNullOrWhiteSpace();
        afterOutage.CatalogLastSyncStatus.ShouldNotContain("super-secret-catalog-token");

        // Recover external catalog -> manual sync converges cleanly to v3
        handler.CatalogOutage503 = false;
        var recovered = await syncService.SyncAsync(created.Id);
        recovered.Synced.ShouldBeTrue();
        recovered.SyncedVersion.ShouldBe(3);

        var afterRecover = await _cdcAppService.GetAsync(created.Id);
        afterRecover.CatalogLastSyncedVersion.ShouldBe(3);
        afterRecover.CatalogLastSyncedAt.ShouldNotBeNull();
    }

    private sealed class ChaosS3AndCatalogHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _objects;
        private bool _injectConflictOnce;
        public bool CatalogOutage503 { get; set; }

        public ChaosS3AndCatalogHandler(Dictionary<string, string> objects, bool forceConflictOnVersion1Once)
        {
            _objects = objects;
            _injectConflictOnce = forceConflictOnVersion1Once;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/2.1/unity-catalog/", StringComparison.Ordinal))
            {
                if (CatalogOutage503)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("{\"error\":\"upstream busy Bearer super-secret-catalog-token\"}", Encoding.UTF8, "application/json"),
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
            }

            if (request.Method == HttpMethod.Get)
            {
                return _objects.TryGetValue(path, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Put)
            {
                if (_injectConflictOnce && path.EndsWith("00000000000000000001.json", StringComparison.Ordinal))
                {
                    _injectConflictOnce = false;
                    _objects[path] = "{\"commitInfo\":{\"txnId\":\"competing-writer\"}}\n";
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }

                if (_objects.ContainsKey(path))
                {
                    return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
                }

                _objects[path] = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
