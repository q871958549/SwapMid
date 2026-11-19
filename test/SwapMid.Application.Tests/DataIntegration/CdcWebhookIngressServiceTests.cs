using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace SwapMid.DataIntegration;

public class CdcWebhookIngressServiceTests
{
    private const string ValidSecret = "whsec_swapmid_v230_super_secret_key_01";

    [Fact]
    public void VerifySignature_Should_Accept_Valid_HmacSha256_And_Reject_Tampered_Or_Malformed()
    {
        var body = Encoding.UTF8.GetBytes("""{"schema":"app","table":"orders","op":"insert","data":{"id":1}}""");
        var sig = CdcWebhookIngressService.ComputeSignature(ValidSecret, body);

        CdcWebhookIngressService.VerifySignature(ValidSecret, body, sig).ShouldBeTrue();
        CdcWebhookIngressService.VerifySignature(ValidSecret, body, sig.ToUpperInvariant()).ShouldBeTrue();
        CdcWebhookIngressService.VerifySignature(ValidSecret, body, null).ShouldBeFalse();
        CdcWebhookIngressService.VerifySignature(ValidSecret, body, "sha256=deadbeef").ShouldBeFalse();
        CdcWebhookIngressService.VerifySignature("wrong_secret_1234567890", body, sig).ShouldBeFalse();

        var tampered = Encoding.UTF8.GetBytes("""{"schema":"app","table":"orders","op":"insert","data":{"id":2}}""");
        CdcWebhookIngressService.VerifySignature(ValidSecret, tampered, sig).ShouldBeFalse();
    }

    [Fact]
    public void EnsurePushWebhookSource_Should_Enforce_RestApi_And_MinSecretLength()
    {
        var nonRest = new DataSource(Guid.NewGuid(), "pg", DataSourceType.RelationalDatabase, "{}");
        Should.Throw<BusinessException>(() => CdcWebhookIngressService.EnsurePushWebhookSource(nonRest));

        var shortSecret = new DataSource(Guid.NewGuid(), "wh-short", DataSourceType.RestApi, """{"webhookSecret":"too_short"}""");
        Should.Throw<BusinessException>(() => CdcWebhookIngressService.EnsurePushWebhookSource(shortSecret));

        var ok = new DataSource(Guid.NewGuid(), "wh-ok", DataSourceType.RestApi, $$"""{"webhookSecret":"{{ValidSecret}}"}""");
        Should.NotThrow(() => CdcWebhookIngressService.EnsurePushWebhookSource(ok));
    }

    [Fact]
    public async Task IngestAsync_Should_Reject_Forged_Signature_Before_Active_Check_With_Zero_Writes()
    {
        var (service, publisher, stream, _) = BuildHarness(activateStream: false);
        var body = Encoding.UTF8.GetBytes("""[{"schema":"shop","table":"orders","op":"insert","data":{"id":1}}]""");

        var res = await service.IngestAsync(stream.Id, body, "sha256=" + new string('0', 64), "del-1");

        res.Outcome.ShouldBe(CdcWebhookIngestOutcome.Unauthorized);
        await publisher.DidNotReceiveWithAnyArgs().PublishIfTopicActiveAsync(default!, default);
    }

    [Fact]
    public async Task IngestAsync_Should_Reject_Inactive_Stream_After_Valid_Signature()
    {
        var (service, publisher, stream, _) = BuildHarness(activateStream: false);
        var body = Encoding.UTF8.GetBytes("""{"schema":"shop","table":"orders","op":"insert","data":{"id":1}}""");
        var sig = CdcWebhookIngressService.ComputeSignature(ValidSecret, body);

        var res = await service.IngestAsync(stream.Id, body, sig, "del-inactive");

        res.Outcome.ShouldBe(CdcWebhookIngestOutcome.Inactive);
        await publisher.DidNotReceiveWithAnyArgs().PublishIfTopicActiveAsync(default!, default);
    }

    [Fact]
    public async Task IngestAsync_Should_Accept_Signed_Batch_Filter_Tables_And_Advance_Monotonic_Checkpoint()
    {
        var (service, publisher, stream, _) = BuildHarness(activateStream: true, monitoredTables: "shop.orders");
        var body = Encoding.UTF8.GetBytes(
            """
            {
              "events": [
                { "schema": "shop", "table": "orders", "op": "insert", "data": { "id": 1001, "amount": 99.5 } },
                { "schema": "shop", "table": "ignored_audit", "op": "insert", "data": { "id": 99 } },
                { "schema": "shop", "table": "orders", "op": "update", "data": { "id": 1001, "amount": 120.0 } }
              ]
            }
            """);
        var sig = CdcWebhookIngressService.ComputeSignature(ValidSecret, body);

        var res = await service.IngestAsync(stream.Id, body, sig, "delivery-batch-001");

        res.Outcome.ShouldBe(CdcWebhookIngestOutcome.Accepted);
        res.Result.ShouldNotBeNull();
        res.Result.DeliveryId.ShouldBe("delivery-batch-001");
        res.Result.AcceptedEventCount.ShouldBe(2);
        res.Result.DeliveredCount.ShouldBe(2);
        res.Result.CheckpointLsn.ShouldBe("webhook:2");
        stream.CheckpointLsn.ShouldBe("webhook:2");
        stream.TotalCapturedCount.ShouldBe(2);
        stream.SchemaBaselineJson.ShouldNotBeNullOrWhiteSpace();
        stream.SchemaBaselineJson.ShouldContain("id");
        stream.SchemaBaselineJson.ShouldContain("amount");
        await publisher.Received(2).PublishIfTopicActiveAsync(Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_Should_Infer_Schema_For_RequiredColumns_And_Pause_With_DeadLetter_On_Drift()
    {
        var ledger = new List<CdcWebhookDeliveryRecord>();
        var (service, publisher, stream, _) = BuildHarness(
            activateStream: true,
            monitoredTables: "shop.orders",
            driftPolicy: CdcSchemaDriftPolicy.PauseAndAlert,
            ledgerStore: ledger);
        stream.ConfigureDataContract(maxFreshnessLagSeconds: 120, maxAllowedWalLagBytes: null, requiredColumns: "id,amount");

        var batch1 = Encoding.UTF8.GetBytes(
            """[{"schema":"shop","table":"orders","op":"insert","data":{"id":1,"amount":55.5}}]""");
        var res1 = await service.IngestAsync(
            stream.Id, batch1, CdcWebhookIngressService.ComputeSignature(ValidSecret, batch1), "del-schema-1");

        res1.Outcome.ShouldBe(CdcWebhookIngestOutcome.Accepted);
        stream.SchemaBaselineJson.ShouldNotBeNullOrWhiteSpace();
        CdcDataContractEvaluator.EvaluateSnapshot(stream, DateTime.UtcNow).Status.ShouldBe(CdcContractStatus.Healthy);

        // Batch 2 drops 'amount' and adds 'currency' -> triggers SchemaDriftPolicy.PauseAndAlert & saves dead-letter!
        var batch2 = Encoding.UTF8.GetBytes(
            """[{"schema":"shop","table":"orders","op":"insert","data":{"id":2,"currency":"CNY"}}]""");
        var res2 = await service.IngestAsync(
            stream.Id, batch2, CdcWebhookIngressService.ComputeSignature(ValidSecret, batch2), "del-schema-drift");

        res2.Outcome.ShouldBe(CdcWebhookIngestOutcome.Inactive);
        stream.Status.ShouldBe(CdcStreamStatus.Paused);
        stream.SchemaDriftSummary.ShouldNotBeNullOrWhiteSpace();
        await publisher.Received(1).PublishIfTopicActiveAsync(Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>());

        var driftLedger = ledger.Single(x => x.DeliveryId == "del-schema-drift");
        driftLedger.Outcome.ShouldBe((int)CdcWebhookIngestOutcome.Inactive);
        driftLedger.CanReplay.ShouldBeTrue();
        driftLedger.StoredPayloadJson.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReplayDeliveryAsync_Should_Replay_Inactive_DeadLetter_And_Forbid_Unauthorized_Replay()
    {
        var ledger = new List<CdcWebhookDeliveryRecord>();
        var (service, publisher, stream, _) = BuildHarness(activateStream: false, ledgerStore: ledger);
        var body = Encoding.UTF8.GetBytes("""{"schema":"shop","table":"orders","op":"insert","data":{"id":77,"total":12.5}}""");

        // 401 -> ledger row has NO stored payload
        var forged = await service.IngestAsync(stream.Id, body, "sha256=" + new string('f', 64), "del-forged");
        forged.Outcome.ShouldBe(CdcWebhookIngestOutcome.Unauthorized);
        var forgedRecord = ledger.Single(x => x.DeliveryId == "del-forged");
        forgedRecord.StoredPayloadJson.ShouldBeNull();
        forgedRecord.CanReplay.ShouldBeFalse();

        // 409 -> ledger row stores bounded payload & is replayable
        var sig = CdcWebhookIngressService.ComputeSignature(ValidSecret, body);
        var inactive = await service.IngestAsync(stream.Id, body, sig, "del-paused");
        inactive.Outcome.ShouldBe(CdcWebhookIngestOutcome.Inactive);
        var deadLetter = ledger.Single(x => x.DeliveryId == "del-paused");
        deadLetter.CanReplay.ShouldBeTrue();

        // Activate stream & replay dead-letter -> Accepted & payload cleared
        stream.Activate();
        var replayed = await service.ReplayDeliveryAsync(stream.Id, deadLetter.Id);
        replayed.AcceptedEventCount.ShouldBe(1);
        replayed.DeliveredCount.ShouldBe(1);
        replayed.CheckpointLsn.ShouldBe("webhook:1");
        deadLetter.Outcome.ShouldBe((int)CdcWebhookIngestOutcome.Accepted);
        deadLetter.ReplayCount.ShouldBe(1);
        deadLetter.CanReplay.ShouldBeFalse();
        deadLetter.StoredPayloadJson.ShouldBeNull();
        await publisher.Received(1).PublishIfTopicActiveAsync(Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>());
    }

    private static (
        CdcWebhookIngressService Service,
        IIntegrationEventPublisher Publisher,
        CdcStreamDefinition Stream,
        DataSource Source) BuildHarness(
        bool activateStream,
        string? monitoredTables = null,
        CdcSchemaDriftPolicy driftPolicy = CdcSchemaDriftPolicy.Ignore,
        List<CdcWebhookDeliveryRecord>? ledgerStore = null)
    {
        var source = new DataSource(
            Guid.NewGuid(),
            "webhook-auth-src",
            DataSourceType.RestApi,
            $$"""{"webhookSecret":"{{ValidSecret}}"}""");

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-push-webhook",
            source.Id,
            "push_webhook",
            "webhook_orders",
            CdcTargetMode.MessageBusTopic,
            targetTopicName: "cdc.events",
            monitoredTables: monitoredTables,
            schemaDriftPolicy: driftPolicy,
            sourceType: CdcSourceType.PushWebhook);
        if (activateStream)
        {
            stream.Activate();
        }

        var streamRepo = Substitute.For<ICdcStreamRepository>();
        streamRepo.FindAsync(stream.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(stream);
        streamRepo.GetAsync(stream.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(stream);
        streamRepo.UpdateAsync(Arg.Any<CdcStreamDefinition>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<CdcStreamDefinition>());

        var sourceRepo = Substitute.For<IRepository<DataSource, Guid>>();
        sourceRepo.FindAsync(source.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(source);

        var publisher = Substitute.For<IIntegrationEventPublisher>();
        publisher.PublishIfTopicActiveAsync(Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var uow = Substitute.For<IUnitOfWork>();
        var uowManager = Substitute.For<IUnitOfWorkManager>();
        uowManager.Begin(Arg.Any<AbpUnitOfWorkOptions>(), Arg.Any<bool>()).Returns(uow);

        var lockHandle = Substitute.For<IAbpDistributedLockHandle>();
        var distributedLock = Substitute.For<IAbpDistributedLock>();
        distributedLock.TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(lockHandle);

        IRepository<CdcWebhookDeliveryRecord, Guid>? deliveryRepo = null;
        if (ledgerStore != null)
        {
            deliveryRepo = Substitute.For<IRepository<CdcWebhookDeliveryRecord, Guid>>();
            deliveryRepo.InsertAsync(Arg.Any<CdcWebhookDeliveryRecord>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var rec = ci.Arg<CdcWebhookDeliveryRecord>();
                    ledgerStore.Add(rec);
                    return rec;
                });
            deliveryRepo.FindAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(ci => ledgerStore.FirstOrDefault(x => x.Id == ci.Arg<Guid>()));
            deliveryRepo.UpdateAsync(Arg.Any<CdcWebhookDeliveryRecord>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<CdcWebhookDeliveryRecord>());
        }

        var connFactory = Substitute.For<IEtlRelationalConnectionFactory>();
        var failurePolicy = new PipelineExecutionFailurePolicy(
            Substitute.For<Volo.Abp.Tracing.ICorrelationIdProvider>(),
            Substitute.For<Microsoft.Extensions.Localization.IStringLocalizer<SwapMid.Localization.SwapMidResource>>());
        var service = new CdcWebhookIngressService(
            streamRepo,
            sourceRepo,
            publisher,
            executionCoordinator: null!,
            connFactory,
            distributedLock,
            uowManager,
            failurePolicy,
            NullLogger<CdcWebhookIngressService>.Instance,
            deliveryRepository: deliveryRepo);

        return (service, publisher, stream, source);
    }
}
