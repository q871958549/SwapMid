using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Schema;
using Shouldly;
using SwapMid.Infrastructure;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

/// <summary>
/// v1.2.0 分析型与事件流接收端（ClickHouse / ObjectStorage Parquet / Kafka）
/// 配置脱敏边界、SigV4 签名、SSRF 策略、Parquet 序列化及 HTTP 接收端写入契约测试。
/// </summary>
public class AnalyticalSinkContractsAndWritersTests
{
    [Fact]
    public void 能力目录_包含ClickHouse与ObjectStorage与Kafka且声明正确写模式()
    {
        var all = ConnectorCapabilityCatalog.GetAll();
        var ch = all.Single(x => x.Provider == "clickhouse");
        var s3 = all.Single(x => x.Provider == "s3-parquet");
        var kafka = all.Single(x => x.Provider == "kafka");

        ch.DataSourceType.ShouldBe(DataSourceType.ClickHouse);
        ch.SupportsTargetWrite.ShouldBeTrue();
        ch.SupportsAtomicUpsert.ShouldBeFalse();
        ch.SupportsTargetReceiptV1.ShouldBeFalse();
        ch.SupportedWriteModes.ShouldBe(new[] { "append", "truncate" });

        s3.DataSourceType.ShouldBe(DataSourceType.ObjectStorage);
        s3.SupportsTargetWrite.ShouldBeTrue();
        s3.SupportedWriteModes.ShouldBe(new[] { "append" });

        kafka.DataSourceType.ShouldBe(DataSourceType.Kafka);
        kafka.SupportsTargetWrite.ShouldBeTrue();
        kafka.SupportedWriteModes.ShouldBe(new[] { "append" });
    }

    [Fact]
    public void ClickHouse_脱敏读取与同作用域免密复用_以及跨作用域拒绝()
    {
        const string initial = """
            {
              "host": "CH.ANALYTICS.TEST.",
              "port": 8443,
              "database": "analytics",
              "username": "etl_writer",
              "password": "ch-top-secret-password",
              "useTls": true,
              "trustServerCertificate": false,
              "targetResourceGroup": "rg-ch-prod"
            }
            """;

        var sanitized = DataSourceConfigProtector.SanitizeForRead(DataSourceType.ClickHouse, initial);
        sanitized.HasSecret.ShouldBeTrue();
        sanitized.Config.ShouldNotContain("ch-top-secret-password");
        var readObj = JsonNode.Parse(sanitized.Config)!.AsObject();
        readObj["host"]!.GetValue<string>().ShouldBe("ch.analytics.test");
        readObj["port"]!.GetValue<int>().ShouldBe(8443);
        readObj["targetResourceGroup"]!.GetValue<string>().ShouldBe("rg-ch-prod");

        const string sameScopeWithoutPassword = """
            {
              "host": "ch.analytics.test",
              "port": 8443,
              "database": "analytics",
              "username": "etl_writer",
              "useTls": true,
              "trustServerCertificate": false,
              "targetResourceGroup": "rg-ch-prod"
            }
            """;
        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.ClickHouse,
            initial,
            DataSourceType.ClickHouse,
            sameScopeWithoutPassword);
        merged.ShouldContain("ch-top-secret-password");

        const string changedHostWithoutPassword = """
            {
              "host": "evil.analytics.test",
              "port": 8443,
              "database": "analytics",
              "username": "etl_writer",
              "useTls": true,
              "trustServerCertificate": false,
              "targetResourceGroup": "rg-ch-prod"
            }
            """;
        Should.Throw<DataSourceCredentialScopeChangedException>(() =>
            DataSourceConfigProtector.MergeForWrite(
                DataSourceType.ClickHouse,
                initial,
                DataSourceType.ClickHouse,
                changedHostWithoutPassword));
    }

    [Fact]
    public void ObjectStorage_脱敏读取与SecretKey作用域守卫()
    {
        const string initial = """
            {
              "provider": "minio",
              "endpoint": "https://minio.example.test:9000",
              "region": "us-east-1",
              "bucket": "swapmid-lake",
              "prefix": "warehouse",
              "format": "parquet",
              "accessKey": "AKIA_MINIO",
              "secretKey": "minio-secret-key-999",
              "targetResourceGroup": "rg-s3-prod"
            }
            """;

        var sanitized = DataSourceConfigProtector.SanitizeForRead(DataSourceType.ObjectStorage, initial);
        sanitized.HasSecret.ShouldBeTrue();
        sanitized.Config.ShouldNotContain("minio-secret-key-999");
        var readObj = JsonNode.Parse(sanitized.Config)!.AsObject();
        readObj["bucket"]!.GetValue<string>().ShouldBe("swapmid-lake");
        readObj["pathStyle"]!.GetValue<bool>().ShouldBeTrue();

        const string sameScopeNoSecret = """
            {
              "provider": "minio",
              "endpoint": "https://minio.example.test:9000",
              "region": "us-east-1",
              "bucket": "swapmid-lake",
              "prefix": "warehouse/v2",
              "format": "jsonl",
              "accessKey": "AKIA_MINIO",
              "pathStyle": true,
              "targetResourceGroup": "rg-s3-prod"
            }
            """;
        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.ObjectStorage,
            initial,
            DataSourceType.ObjectStorage,
            sameScopeNoSecret);
        merged.ShouldContain("minio-secret-key-999");

        const string changedBucketNoSecret = """
            {
              "provider": "minio",
              "endpoint": "https://minio.example.test:9000",
              "region": "us-east-1",
              "bucket": "other-bucket",
              "accessKey": "AKIA_MINIO",
              "pathStyle": true,
              "targetResourceGroup": "rg-s3-prod"
            }
            """;
        Should.Throw<DataSourceCredentialScopeChangedException>(() =>
            DataSourceConfigProtector.MergeForWrite(
                DataSourceType.ObjectStorage,
                initial,
                DataSourceType.ObjectStorage,
                changedBucketNoSecret));
    }

    [Fact]
    public void Kafka_Sasl脱敏读取与作用域守卫_以及Topic目标校验()
    {
        const string initial = """
            {
              "bootstrapServers": "broker1.example.test:9092, broker2.example.test:9092",
              "clientId": "swapmid-prod",
              "securityProtocol": "SaslSsl",
              "saslMechanism": "ScramSha256",
              "saslUsername": "kafka_producer",
              "saslPassword": "kafka-sasl-secret-123",
              "acks": "All",
              "compressionType": "Snappy",
              "targetResourceGroup": "rg-kafka-prod"
            }
            """;

        var sanitized = DataSourceConfigProtector.SanitizeForRead(DataSourceType.Kafka, initial);
        sanitized.HasSecret.ShouldBeTrue();
        sanitized.Config.ShouldNotContain("kafka-sasl-secret-123");

        const string sameScopeNoPass = """
            {
              "bootstrapServers": "broker1.example.test:9092,broker2.example.test:9092",
              "clientId": "swapmid-prod",
              "securityProtocol": "SaslSsl",
              "saslMechanism": "ScramSha256",
              "saslUsername": "kafka_producer",
              "acks": "All",
              "compressionType": "Zstd",
              "targetResourceGroup": "rg-kafka-prod"
            }
            """;
        var merged = DataSourceConfigProtector.MergeForWrite(
            DataSourceType.Kafka,
            initial,
            DataSourceType.Kafka,
            sameScopeNoPass);
        merged.ShouldContain("kafka-sasl-secret-123");

        var validKafkaTransform = EtlTransformConfig.Parse(
            """{"sourceQuery":"SELECT id, amount FROM orders","targetTable":"orders.events-v1","mode":"append"}""",
            DataSourceType.Kafka);
        validKafkaTransform.TargetTable.ShouldBe("orders.events-v1");

        var validS3Transform = EtlTransformConfig.Parse(
            """{"sourceQuery":"SELECT id FROM orders","targetTable":"analytics/orders-daily","mode":"append"}""",
            DataSourceType.ObjectStorage);
        validS3Transform.TargetTable.ShouldBe("analytics/orders-daily");

        Should.Throw<BusinessException>(() => EtlTransformConfig.Parse(
            """{"sourceQuery":"SELECT id FROM orders","targetTable":"orders.events-v1","mode":"append-receipt-v1"}""",
            DataSourceType.Kafka));
        Should.Throw<BusinessException>(() => EtlTransformConfig.Parse(
            """{"sourceQuery":"SELECT id FROM orders","targetTable":"orders","mode":"upsert","keyColumns":["id"]}""",
            DataSourceType.ClickHouse));
    }

    [Fact]
    public void AwsSigV4Signer_生成标准Authorization与CanonicalHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://minio.example.test:9000/swapmid-lake?max-keys=0&list-type=2");
        AwsSigV4Signer.Sign(
            request,
            "us-east-1",
            "AKIA_TEST",
            "SECRET_TEST",
            AwsSigV4Signer.EmptyPayloadSha256Hex,
            new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));

        request.Headers.Host.ShouldBe("minio.example.test:9000");
        request.Headers.GetValues("x-amz-date").Single().ShouldBe("20261115T120000Z");
        request.Headers.GetValues("x-amz-content-sha256").Single().ShouldBe(AwsSigV4Signer.EmptyPayloadSha256Hex);
        var auth = request.Headers.GetValues("Authorization").Single();
        auth.ShouldStartWith("AWS4-HMAC-SHA256 Credential=AKIA_TEST/20261115/us-east-1/s3/aws4_request, SignedHeaders=host;x-amz-content-sha256;x-amz-date, Signature=");
        auth.Split("Signature=")[1].Length.ShouldBe(64);
    }

    [Fact]
    public async Task SinkEndpointPolicy_拦截未加白与回环元数据地址()
    {
        var emptyConfig = new ConfigurationBuilder().AddInMemoryCollection().Build();
        await Should.ThrowAsync<BusinessException>(() =>
            DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
                new Uri("http://8.8.8.8:8123/"),
                emptyConfig,
                CancellationToken.None));

        var allowLoopbackHost = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedHosts] = "127.0.0.1,169.254.169.254,8.8.8.8,10.20.30.40",
                [ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedPrivateNetworks] = "10.0.0.0/8",
            })
            .Build();

        await Should.ThrowAsync<BusinessException>(() =>
            DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
                new Uri("http://127.0.0.1:8123/"),
                allowLoopbackHost,
                CancellationToken.None));
        await Should.ThrowAsync<BusinessException>(() =>
            DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
                new Uri("http://169.254.169.254/latest/meta-data/"),
                allowLoopbackHost,
                CancellationToken.None));

        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            new Uri("http://8.8.8.8:8123/"),
            allowLoopbackHost,
            CancellationToken.None);
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            new Uri("http://10.20.30.40:9000/"),
            allowLoopbackHost,
            CancellationToken.None);
    }

    [Fact]
    public async Task ParquetSinkWriter_流式多RowGroup二进制往返读写完整准确()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"swapmid-parquet-test-{Guid.NewGuid():N}.parquet");
        var guid1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var dt1 = new DateTime(2026, 11, 15, 8, 30, 0, DateTimeKind.Utc);

        try
        {
            var rowsWritten = await ObjectStorageEtlSinkWriter.WriteParquetTempFileAsync(
                tempFile,
                async (onBatch, ct) =>
                {
                    var cols = new[] { "id", "name", "amount", "is_active", "created_on", "created_at", "trace_id", "payload" };
                    var types = new[] { typeof(long), typeof(string), typeof(decimal), typeof(bool), typeof(DateOnly), typeof(DateTime), typeof(Guid), typeof(byte[]) };
                    await onBatch(
                        new EtlSourceBatch(
                            cols,
                            types,
                            new List<object?[]>
                            {
                                new object?[] { 101L, "Alpha", 123.45m, true, new DateOnly(2026, 11, 15), dt1, guid1, new byte[] { 1, 2, 3 } },
                                new object?[] { 102L, null, DBNull.Value, false, null, null, null, null },
                            },
                            0),
                        ct);

                    await onBatch(
                        new EtlSourceBatch(
                            cols,
                            types,
                            new List<object?[]>
                            {
                                new object?[] { 103L, "Beta", 99.50m, true, new DateOnly(2026, 11, 16), dt1.AddDays(1), guid1, new byte[] { 9, 8 } },
                            },
                            1),
                        ct);
                },
                CancellationToken.None);

            rowsWritten.ShouldBe(3);
            File.Exists(tempFile).ShouldBeTrue();

            var bytes = await File.ReadAllBytesAsync(tempFile);
            Encoding.ASCII.GetString(bytes, 0, 4).ShouldBe("PAR1");

            await using var readStream = File.OpenRead(tempFile);
            await using var reader = await ParquetReader.CreateAsync(readStream);
            reader.RowGroupCount.ShouldBe(2);
            var dataFields = reader.Schema.GetDataFields();
            dataFields.Select(f => f.Name)
                .ShouldBe(new[] { "id", "name", "amount", "is_active", "created_on", "created_at", "trace_id", "payload" });

            using var rg0 = reader.OpenRowGroupReader(0);
            var ids0 = new long?[2];
            var names0 = new string?[2];
            var amounts0 = new decimal?[2];
            var active0 = new bool?[2];
            var guids0 = new Guid?[2];
            var payloads0 = new byte[2][];
            await rg0.ReadAsync<long>(dataFields[0], ids0);
            await rg0.ReadAsync(dataFields[1], names0!);
            await rg0.ReadAsync<decimal>(dataFields[2], amounts0);
            await rg0.ReadAsync<bool>(dataFields[3], active0);
            await rg0.ReadAsync<Guid>(dataFields[6], guids0);
            await rg0.ReadAsync(dataFields[7], payloads0!);
            ids0.ShouldBe(new long?[] { 101L, 102L });
            names0.ShouldBe(new string?[] { "Alpha", null });
            amounts0.ShouldBe(new decimal?[] { 123.45m, null });
            active0.ShouldBe(new bool?[] { true, false });
            guids0.ShouldBe(new Guid?[] { guid1, null });
            payloads0[0].ShouldBe(new byte[] { 1, 2, 3 });
            payloads0[1].ShouldBeNull();

            using var rg1 = reader.OpenRowGroupReader(1);
            var names1 = new string?[1];
            await rg1.ReadAsync(dataFields[1], names1!);
            names1.ShouldBe(new string?[] { "Beta" });
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void TargetIdentity_ClickHouse与ObjectStorage与Kafka生成稳定资源指纹并强制ResourceGroup()
    {
        var chTarget = new DataSource(
            Guid.NewGuid(),
            "ch-id",
            DataSourceType.ClickHouse,
            """{"host":"8.8.8.8","port":8123,"database":"analytics","targetResourceGroup":"rg-ch"}""");
        var chIdentity = DataPipelineExecutionCoordinator.ComputeTargetIdentity(
            chTarget,
            """{"sourceQuery":"SELECT id FROM a","targetTable":"orders","mode":"append"}""");
        chIdentity.Provider.ShouldBe("clickhouse");
        chIdentity.TargetTable.ShouldBe("analytics.orders");
        chIdentity.TargetResourceGroup.ShouldBe("rg-ch");

        var s3Target = new DataSource(
            Guid.NewGuid(),
            "s3-id",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"https://8.8.8.8:9000","bucket":"lake","prefix":"ods","accessKey":"AK","secretKey":"SK","targetResourceGroup":"rg-s3"}""");
        var s3Identity = DataPipelineExecutionCoordinator.ComputeTargetIdentity(
            s3Target,
            """{"sourceQuery":"SELECT id FROM a","targetTable":"orders-v1","mode":"append"}""");
        s3Identity.Provider.ShouldBe("s3-parquet");
        s3Identity.TargetResourceGroup.ShouldBe("rg-s3");

        var kafkaNoRg = new DataSource(
            Guid.NewGuid(),
            "kafka-no-rg",
            DataSourceType.Kafka,
            """{"bootstrapServers":"8.8.8.8:9092"}""");
        var ex = Should.Throw<BusinessException>(() => DataPipelineExecutionCoordinator.ComputeTargetIdentity(
            kafkaNoRg,
            """{"sourceQuery":"SELECT id FROM a","targetTable":"orders.events-v1","mode":"append"}"""));
        ex.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.PipelineTargetResourceGroupRequired);
    }

    [Fact]
    public async Task ClickHouse与ObjectStorage写入器_正确发送Truncate与JSONEachRow及SigV4Put请求()
    {
        var recorded = new List<(HttpMethod Method, Uri Uri, string? UserHeader, string? KeyHeader, string? AuthHeader, byte[] Body)>();
        var handler = new RecordingHttpHandler(async request =>
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsByteArrayAsync()
                : Array.Empty<byte>();
            string? Header(string name) => request.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
            recorded.Add((
                request.Method,
                request.RequestUri!,
                Header("X-ClickHouse-User"),
                Header("X-ClickHouse-Key"),
                Header("Authorization"),
                body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });

        var factory = new SingleHandlerHttpClientFactory(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ExternalHttpConfigurationKeys.DataIntegrationSinkAllowedHosts] = "8.8.8.8",
            })
            .Build();

        var chWriter = new ClickHouseEtlSinkWriter(factory, config);
        var chTarget = new DataSource(
            Guid.NewGuid(),
            "ch-test",
            DataSourceType.ClickHouse,
            """{"host":"8.8.8.8","port":8123,"database":"analytics","username":"writer","password":"secret-ch","targetResourceGroup":"rg-ch"}""");
        var chTransform = EtlTransformConfig.Parse(
            """{"sourceQuery":"SELECT id, title FROM src","targetTable":"orders_fact","mode":"truncate"}""",
            DataSourceType.ClickHouse);

        var chCount = await chWriter.WriteAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            chTarget,
            chTransform,
            async (onBatch, ct) =>
            {
                await onBatch(
                    new EtlSourceBatch(
                        new[] { "id", "title" },
                        new[] { typeof(int), typeof(string) },
                        new List<object?[]> { new object?[] { 1, "First" }, new object?[] { 2, "Second" } },
                        0),
                    ct);
            },
            CancellationToken.None);

        chCount.ShouldBe(2);
        recorded.Count.ShouldBe(2);
        Encoding.UTF8.GetString(recorded[0].Body).ShouldBe("TRUNCATE TABLE IF EXISTS `analytics`.`orders_fact`");
        recorded[0].UserHeader.ShouldBe("writer");
        recorded[0].KeyHeader.ShouldBe("secret-ch");
        var insertBody = Encoding.UTF8.GetString(recorded[1].Body);
        insertBody.ShouldStartWith("INSERT INTO `analytics`.`orders_fact` FORMAT JSONEachRow\n");
        insertBody.ShouldContain("""{"id":1,"title":"First"}""");

        recorded.Clear();
        var s3Writer = new ObjectStorageEtlSinkWriter(factory, config);
        var s3Target = new DataSource(
            Guid.NewGuid(),
            "s3-test",
            DataSourceType.ObjectStorage,
            """{"provider":"minio","endpoint":"https://8.8.8.8:9000","region":"us-east-1","bucket":"lakehouse","prefix":"ods","format":"parquet","accessKey":"AK","secretKey":"SK","targetResourceGroup":"rg-s3"}""");
        var s3Transform = EtlTransformConfig.Parse(
            """{"sourceQuery":"SELECT id, title FROM src","targetTable":"orders/daily","mode":"append"}""",
            DataSourceType.ObjectStorage);
        var runId = Guid.NewGuid();

        var s3Count = await s3Writer.WriteAsync(
            Guid.NewGuid(),
            runId,
            s3Target,
            s3Transform,
            async (onBatch, ct) =>
            {
                await onBatch(
                    new EtlSourceBatch(
                        new[] { "id", "title" },
                        new[] { typeof(int), typeof(string) },
                        new List<object?[]> { new object?[] { 10, "ParquetRow" } },
                        0),
                    ct);
            },
            CancellationToken.None);

        s3Count.ShouldBe(1);
        recorded.Count.ShouldBe(1);
        recorded[0].Method.ShouldBe(HttpMethod.Put);
        recorded[0].Uri.AbsolutePath.ShouldBe($"/lakehouse/ods/orders/daily/run_{runId:N}.parquet");
        recorded[0].AuthHeader.ShouldNotBeNull();
        recorded[0].AuthHeader!.ShouldStartWith("AWS4-HMAC-SHA256 ");
        Encoding.ASCII.GetString(recorded[0].Body, 0, 4).ShouldBe("PAR1");
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public RecordingHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request);
    }

    private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
