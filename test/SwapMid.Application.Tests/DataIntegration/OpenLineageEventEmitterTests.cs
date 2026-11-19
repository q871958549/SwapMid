using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SwapMid.Infrastructure;
using Xunit;

namespace SwapMid.DataIntegration;

public class OpenLineageEventEmitterTests
{
    [Fact]
    public async Task Emitter_Should_Be_FailClosed_When_Disabled_And_Emit_V2_RunEvent_When_Enabled()
    {
        var capturedBodies = new List<string>();
        var handler = new CaptureHandler(capturedBodies);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ExternalHttpClientNames.DataIntegrationSink)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var stream = new CdcStreamDefinition(
            Guid.NewGuid(),
            "orders-wap-ol",
            Guid.NewGuid(),
            "pub_orders",
            "slot_orders",
            CdcTargetMode.ExternalSink,
            monitoredTables: "public.orders",
            targetSinkId: Guid.NewGuid(),
            targetSinkDestination: "orders/wap",
            lakehouseTableFormat: LakehouseTableFormat.ApacheIceberg);

        // 1. Default fail-closed (OpenLineage:Enabled omitted/false)
        var disabledConfig = new ConfigurationBuilder().Build();
        var disabledEmitter = new OpenLineageEventEmitter(factory, disabledConfig, NullLogger<OpenLineageEventEmitter>.Instance);
        (await disabledEmitter.TryEmitLakehouseLifecycleEventAsync("promote", stream, "orders/wap", 2, 1002, 3, "staging"))
            .ShouldBeFalse();
        capturedBodies.ShouldBeEmpty();

        // 2. Enabled + SSRF-allowlisted endpoint -> emits CNCF OpenLineage v2-0-2 COMPLETE event
        var enabledConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenLineage:Enabled"] = "true",
                ["OpenLineage:EndpointUrl"] = "http://198.51.100.44:5000/api/v1/lineage",
                ["OpenLineage:Namespace"] = "swapmid-prod",
                ["DataIntegration:Sinks:AllowedHosts"] = "198.51.100.44",
            })
            .Build();
        var enabledEmitter = new OpenLineageEventEmitter(factory, enabledConfig, NullLogger<OpenLineageEventEmitter>.Instance);
        (await enabledEmitter.TryEmitLakehouseLifecycleEventAsync("rollback", stream, "orders/wap", 1, 1001, 4, "audit-v1"))
            .ShouldBeTrue();
        capturedBodies.Count.ShouldBe(1);
        capturedBodies[0].ShouldContain("\"eventType\":\"COMPLETE\"");
        capturedBodies[0].ShouldContain(OpenLineageEventEmitter.ProducerUri);
        capturedBodies[0].ShouldContain(OpenLineageEventEmitter.SpecSchemaUrl);
        capturedBodies[0].ShouldContain("\"namespace\":\"swapmid-prod\"");
        capturedBodies[0].ShouldContain("cdc.orders-wap-ol.rollback");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly List<string> _bodies;
        public CaptureHandler(List<string> bodies) => _bodies = bodies;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
