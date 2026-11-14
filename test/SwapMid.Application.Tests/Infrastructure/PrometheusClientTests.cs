using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace SwapMid.Infrastructure;

public class PrometheusClientTests
{
    [Fact]
    public async Task 查询完成后应释放响应及其内容()
    {
        var content = new TrackingContent(
            """{"status":"success","data":{"resultType":"vector","result":[{"metric":{"job":"han-api"},"value":[1,"1"]}]}}""");
        var handler = new StubHttpMessageHandler
        {
            Handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
            }),
        };
        using var http = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prometheus:BaseUrl"] = "https://prometheus.test",
            })
            .Build();
        var client = new PrometheusClient(
            configuration,
            new SingleClientFactory(http),
            NullLogger<PrometheusClient>.Instance);

        var samples = await client.QueryVectorAsync("up");

        samples.Count.ShouldBe(1);
        content.IsDisposed.ShouldBeTrue();
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            name.ShouldBe(ExternalHttpClientNames.Prometheus);
            return client;
        }
    }

    private sealed class TrackingContent(string content) : StringContent(content)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
