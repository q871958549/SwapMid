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
        await publisher.Received(2).PublishIfTopicActiveAsync(Arg.Any<IntegrationEventEnvelope>(), Arg.Any<CancellationToken>());
    }

    private static (
        CdcWebhookIngressService Service,
        IIntegrationEventPublisher Publisher,
        CdcStreamDefinition Stream,
        DataSource Source) BuildHarness(bool activateStream, string? monitoredTables = null)
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
            NullLogger<CdcWebhookIngressService>.Instance);

        return (service, publisher, stream, source);
    }
}
