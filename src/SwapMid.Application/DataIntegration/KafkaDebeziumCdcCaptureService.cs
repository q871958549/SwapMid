using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SwapMid.Monitoring;
using SwapMid.SharedKernel.IntegrationEvents;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

internal sealed record KafkaPartitionWatermark(int Partition, long LowOffset, long HighOffset);

internal sealed record KafkaConsumedRecord(
    int Partition,
    long Offset,
    string? Value,
    DateTime TimestampUtc,
    byte[]? RawValue = null);

internal interface IKafkaCdcBrokerSession : IDisposable
{
    IReadOnlyList<KafkaPartitionWatermark> ProbeTopicWatermarks(string topic, TimeSpan timeout);

    IReadOnlyList<KafkaConsumedRecord> PollBounded(
        string topic,
        IReadOnlyDictionary<int, long> partitionStartOffsets,
        IReadOnlyDictionary<int, long> partitionHighWatermarks,
        int maxRecords,
        TimeSpan maxDuration,
        CancellationToken cancellationToken);
}

/// <summary>
/// Apache Kafka / Redpanda Debezium 信封入站 CDC 捕获服务（<see cref="CdcSourceType.KafkaDebezium"/>）：
/// 基于受 SSRF 保护的 Kafka <see cref="DataSource"/>，执行 Topic 分区水位预检、有界分区拉取、
/// Debezium JSON / CloudEvents 解析、自动 Schema 推导与漂移护栏，并在下游成功投递后单调推进分区偏移量位点。
/// </summary>
public sealed class KafkaDebeziumCdcCaptureService : ITransientDependency
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollMaxDuration = TimeSpan.FromSeconds(6);

    private readonly IConfiguration _configuration;
    private readonly IIntegrationEventPublisher _integrationEventPublisher;
    private readonly DataPipelineExecutionCoordinator _executionCoordinator;
    private readonly IReadOnlyDictionary<DataSourceType, IEtlSinkWriter> _sinkWriters;
    private readonly IEtlRelationalConnectionFactory _connectionFactory;
    private readonly ILogger<KafkaDebeziumCdcCaptureService> _logger;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly Func<KafkaSinkConfig, string, IKafkaCdcBrokerSession> _sessionFactory;

    public KafkaDebeziumCdcCaptureService(
        IConfiguration configuration,
        IIntegrationEventPublisher integrationEventPublisher,
        DataPipelineExecutionCoordinator executionCoordinator,
        IEtlRelationalConnectionFactory connectionFactory,
        ILogger<KafkaDebeziumCdcCaptureService> logger,
        IEnumerable<IEtlSinkWriter>? sinkWriters = null,
        IHttpClientFactory? httpClientFactory = null)
        : this(
            configuration,
            integrationEventPublisher,
            executionCoordinator,
            connectionFactory,
            logger,
            sinkWriters,
            sessionFactory: null,
            httpClientFactory)
    {
    }

    internal KafkaDebeziumCdcCaptureService(
        IConfiguration configuration,
        IIntegrationEventPublisher integrationEventPublisher,
        DataPipelineExecutionCoordinator executionCoordinator,
        IEtlRelationalConnectionFactory connectionFactory,
        ILogger<KafkaDebeziumCdcCaptureService> logger,
        IEnumerable<IEtlSinkWriter>? sinkWriters,
        Func<KafkaSinkConfig, string, IKafkaCdcBrokerSession>? sessionFactory,
        IHttpClientFactory? httpClientFactory = null)
    {
        _configuration = configuration;
        _integrationEventPublisher = integrationEventPublisher;
        _executionCoordinator = executionCoordinator;
        _connectionFactory = connectionFactory;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _sinkWriters = (sinkWriters ?? Array.Empty<IEtlSinkWriter>())
            .GroupBy(x => x.TargetType)
            .ToDictionary(g => g.Key, g => g.First());
        _sessionFactory = sessionFactory ?? ((cfg, groupId) => new ConfluentKafkaCdcBrokerSession(cfg, groupId));
    }

    internal static void EnsureKafkaSource(DataSource source)
    {
        if (source.Type != DataSourceType.Kafka)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcStreamConfigInvalid)
                .WithData("Reason", $"Kafka Debezium CDC 源必须绑定 Kafka 类型数据源，当前为 {source.Type}。");
        }

        _ = KafkaSinkConfigContract.ParseForWrite(source.Config, requirePasswordIfSasl: false);
    }

    internal async Task<CdcPreflightResultDto> PreflightAsync(
        DataSource source,
        CdcStreamDefinition stream,
        CancellationToken cancellationToken)
    {
        EnsureKafkaSource(source);
        var cfg = KafkaSinkConfigContract.ParseForWrite(source.Config, requirePasswordIfSasl: true);
        await DataIntegrationSinkEndpointPolicy.ValidateKafkaBrokersAsync(cfg.Brokers, _configuration, cancellationToken);

        var topic = stream.PublicationName.Trim();
        var savedOffsets = KafkaDebeziumEnvelopeParser.ParseCheckpoints(stream.CheckpointLsn);

        try
        {
            using var session = _sessionFactory(cfg, stream.SlotName);
            var watermarks = session.ProbeTopicWatermarks(topic, ProbeTimeout);
            if (watermarks.Count == 0)
            {
                return new CdcPreflightResultDto
                {
                    IsReady = false,
                    WalLevel = "kafka_debezium",
                    PostgreSqlVersion = "Kafka / Redpanda",
                    PublicationExists = false,
                    SlotExists = true,
                    SlotPlugin = "debezium-json",
                    ConfirmedFlushLsn = stream.CheckpointLsn,
                    Diagnostics = [$"Kafka Broker 中未发现 Topic「{topic}」或其分区元数据为空，请先创建 Topic。"],
                    Summary = $"Kafka Topic「{topic}」不存在或不可用。",
                };
            }

            long totalLag = 0;
            var highMap = new Dictionary<int, long>(watermarks.Count);
            foreach (var wm in watermarks)
            {
                highMap[wm.Partition] = wm.HighOffset;
                var committed = savedOffsets.TryGetValue(wm.Partition, out var c)
                    ? Math.Max(c, wm.LowOffset)
                    : wm.LowOffset;
                totalLag += Math.Max(0L, wm.HighOffset - committed);
            }

            PlatformMetrics.CdcKafkaLagMessages.WithLabels(stream.Name).Set(totalLag);

            var currentWatermarkLsn = KafkaDebeziumEnvelopeParser.FormatCheckpoints(highMap);
            var diagnostics = new List<string>
            {
                $"已连通 Kafka Topic「{topic}」（{watermarks.Count} 个分区，消费组 {stream.SlotName}）。",
                $"当前待消费消息滞留：{totalLag} 条（最新水位 {currentWatermarkLsn}）。",
            };
            var srReady = true;
            var pluginTag = "debezium-json";
            if (!string.IsNullOrWhiteSpace(cfg.SchemaRegistryUrl) && _httpClientFactory != null)
            {
                var srProbe = await ConfluentSchemaRegistryClient.ProbeAsync(_httpClientFactory, _configuration, cfg, cancellationToken);
                srReady = srProbe.Reachable;
                pluginTag = "debezium-avro+json";
                diagnostics.Add(srProbe.Message);
            }

            return new CdcPreflightResultDto
            {
                IsReady = srReady,
                WalLevel = "kafka_debezium",
                PostgreSqlVersion = $"Kafka ({watermarks.Count} partitions)",
                PublicationExists = true,
                SlotExists = true,
                SlotActive = stream.Status == CdcStreamStatus.Active,
                SlotPlugin = pluginTag,
                ConfirmedFlushLsn = stream.CheckpointLsn,
                CurrentWalLsn = currentWatermarkLsn,
                WalLagBytes = totalLag,
                Diagnostics = diagnostics,
                Summary = srReady
                    ? $"Kafka Debezium 源已就绪（Topic {topic}，{watermarks.Count} 分区，滞留 {totalLag} 条）。"
                    : $"Kafka Topic 可达但 Schema Registry 不可达：{diagnostics[^1]}",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var errType = ex.GetType().Name;
            return new CdcPreflightResultDto
            {
                IsReady = false,
                WalLevel = "kafka_debezium",
                PostgreSqlVersion = "Kafka / Redpanda",
                SlotPlugin = "debezium-json",
                Diagnostics = [$"连接 Kafka Broker 或查询 Topic「{topic}」元数据失败（{errType}）。"],
                Summary = $"Kafka Debezium 预检失败（{errType}）。",
            };
        }
    }

    internal async Task<PgOutputCaptureBatchResult> CaptureBatchAsync(
        DataSource source,
        CdcStreamDefinition stream,
        DataSource? targetSink,
        CancellationToken cancellationToken,
        Func<CdcSchemaDriftEvaluation, Task>? onSchemaEvaluated = null)
    {
        EnsureKafkaSource(source);
        var cfg = KafkaSinkConfigContract.ParseForWrite(source.Config, requirePasswordIfSasl: true);
        await DataIntegrationSinkEndpointPolicy.ValidateKafkaBrokersAsync(cfg.Brokers, _configuration, cancellationToken);

        var sw = Stopwatch.StartNew();
        var topic = stream.PublicationName.Trim();
        var savedOffsets = KafkaDebeziumEnvelopeParser.ParseCheckpoints(stream.CheckpointLsn);

        try
        {
            using var session = _sessionFactory(cfg, stream.SlotName);
            var watermarks = session.ProbeTopicWatermarks(topic, ProbeTimeout);
            if (watermarks.Count == 0)
            {
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.CdcReplicationPreconditionFailed)
                    .WithData("Reason", $"Kafka Topic「{topic}」不存在或暂无可用分区。");
            }

            var startOffsets = new Dictionary<int, long>(watermarks.Count);
            var highOffsets = new Dictionary<int, long>(watermarks.Count);
            var nextOffsets = new Dictionary<int, long>(savedOffsets);
            long pendingTotal = 0;

            foreach (var wm in watermarks)
            {
                highOffsets[wm.Partition] = wm.HighOffset;
                var start = savedOffsets.TryGetValue(wm.Partition, out var s)
                    ? Math.Max(s, wm.LowOffset)
                    : wm.LowOffset;
                startOffsets[wm.Partition] = start;
                nextOffsets[wm.Partition] = start;
                pendingTotal += Math.Max(0L, wm.HighOffset - start);
            }

            if (pendingTotal == 0)
            {
                PlatformMetrics.CdcKafkaLagMessages.WithLabels(stream.Name).Set(0);
                PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
                var unchanged = string.IsNullOrWhiteSpace(stream.CheckpointLsn)
                    ? KafkaDebeziumEnvelopeParser.FormatCheckpoints(nextOffsets)
                    : stream.CheckpointLsn;
                return new PgOutputCaptureBatchResult(
                    stream.CheckpointLsn,
                    unchanged,
                    0,
                    0,
                    DateTime.UtcNow,
                    $"Kafka Topic「{topic}」当前无新增消息（水位 {unchanged}）。");
            }

            var consumed = session.PollBounded(
                topic,
                startOffsets,
                highOffsets,
                stream.MaxBatchSize,
                PollMaxDuration,
                cancellationToken);

            var monitored = CdcProtocolHelpers.ParseMonitoredTables(stream.MonitoredTables, defaultSchema: "kafka");
            var matched = new List<PgOutputCapturedChange>(consumed.Count);
            foreach (var rec in consumed)
            {
                var candidateNext = rec.Offset + 1;
                if (!nextOffsets.TryGetValue(rec.Partition, out var cur) || candidateNext > cur)
                {
                    nextOffsets[rec.Partition] = candidateNext;
                }

                PgOutputCapturedChange? change = null;
                if (ConfluentSchemaRegistryClient.IsConfluentWirePayload(rec.RawValue))
                {
                    if (_httpClientFactory == null)
                    {
                        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                            .WithData("Reason", "Schema Registry HTTP 客户端未注册，无法解码 Confluent Avro 二进制消息。");
                    }

                    change = await ConfluentSchemaRegistryClient.TryDecodeConfluentAvroMessageAsync(
                        _httpClientFactory, _configuration, cfg, rec.RawValue!, topic, rec.Partition, rec.Offset, rec.TimestampUtc, cancellationToken);
                }
                else
                {
                    KafkaDebeziumEnvelopeParser.TryParseMessage(
                        rec.Value, topic, rec.Partition, rec.Offset, rec.TimestampUtc, out change);
                }

                if (change != null &&
                    (monitored.Count == 0 || CdcProtocolHelpers.ShouldCaptureRelation(change.Schema, change.Table, monitored)))
                {
                    matched.Add(change);
                }
            }

            var newCheckpoint = KafkaDebeziumEnvelopeParser.FormatCheckpoints(nextOffsets);
            long delivered = 0;
            if (matched.Count > 0)
            {
                var inferred = CdcWebhookSchemaInferenceHelper.InferSchemas(matched);
                var schemaEval = CdcSchemaDriftEvaluator.Compare(stream.SchemaBaselineJson, inferred);
                if (onSchemaEvaluated != null)
                {
                    await onSchemaEvaluated(schemaEval);
                }

                delivered = await CdcStreamDeliveryDispatcher.DeliverAsync(
                    stream,
                    targetSink,
                    matched,
                    batchEndLsn: null,
                    _integrationEventPublisher,
                    _executionCoordinator,
                    _sinkWriters,
                    _connectionFactory,
                    _logger,
                    cancellationToken,
                    eventSequenceOffset: 0,
                    idempotencyKeyOverride: $"kafka:{topic}:{newCheckpoint}");

                foreach (var ev in matched)
                {
                    var opLabel = ev.Operation is "insert" or "update" or "delete" ? ev.Operation : "insert";
                    PlatformMetrics.CdcEventsCapturedTotal.WithLabels(opLabel).Inc();
                }
            }

            long remainingLag = 0;
            foreach (var wm in watermarks)
            {
                var after = nextOffsets.TryGetValue(wm.Partition, out var a) ? a : wm.LowOffset;
                remainingLag += Math.Max(0L, wm.HighOffset - after);
            }

            PlatformMetrics.CdcKafkaLagMessages.WithLabels(stream.Name).Set(remainingLag);
            PlatformMetrics.CdcCapturesTotal.WithLabels("succeeded").Inc();
            return new PgOutputCaptureBatchResult(
                stream.CheckpointLsn,
                newCheckpoint,
                matched.Count,
                delivered,
                DateTime.UtcNow,
                $"已从 Kafka Topic「{topic}」拉取 {consumed.Count} 条消息（命中 {matched.Count} 条，投递 {delivered} 条，剩余滞留 {remainingLag} 条）。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BusinessException)
        {
            PlatformMetrics.CdcCapturesTotal.WithLabels("failed").Inc();
            throw;
        }
        catch (Exception ex)
        {
            PlatformMetrics.CdcCapturesTotal.WithLabels("failed").Inc();
            var errType = ex.GetType().Name;
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", $"Kafka Debezium CDC 捕获失败（{errType}）。");
        }
        finally
        {
            sw.Stop();
            PlatformMetrics.CdcCaptureDuration.Observe(sw.Elapsed.TotalSeconds);
        }
    }

    private sealed class ConfluentKafkaCdcBrokerSession : IKafkaCdcBrokerSession
    {
        private readonly KafkaSinkConfig _config;
        private readonly string _groupId;

        public ConfluentKafkaCdcBrokerSession(KafkaSinkConfig config, string groupId)
        {
            _config = config;
            _groupId = groupId;
        }

        public IReadOnlyList<KafkaPartitionWatermark> ProbeTopicWatermarks(string topic, TimeSpan timeout)
        {
            using var admin = new AdminClientBuilder(_config.ToAdminConfig()).Build();
            var meta = admin.GetMetadata(topic, timeout);
            var topicMeta = meta.Topics.FirstOrDefault(t => string.Equals(t.Topic, topic, StringComparison.Ordinal));
            if (topicMeta == null || topicMeta.Error.IsError || topicMeta.Partitions.Count == 0)
            {
                return Array.Empty<KafkaPartitionWatermark>();
            }

            using var consumer = new ConsumerBuilder<Ignore, string>(_config.ToConsumerConfig(_groupId)).Build();
            var results = new List<KafkaPartitionWatermark>(topicMeta.Partitions.Count);
            foreach (var part in topicMeta.Partitions.OrderBy(p => p.PartitionId))
            {
                var wm = consumer.QueryWatermarkOffsets(new TopicPartition(topic, part.PartitionId), timeout);
                results.Add(new KafkaPartitionWatermark(
                    part.PartitionId,
                    Math.Max(0L, wm.Low.Value),
                    Math.Max(0L, wm.High.Value)));
            }

            return results;
        }

        public IReadOnlyList<KafkaConsumedRecord> PollBounded(
            string topic,
            IReadOnlyDictionary<int, long> partitionStartOffsets,
            IReadOnlyDictionary<int, long> partitionHighWatermarks,
            int maxRecords,
            TimeSpan maxDuration,
            CancellationToken cancellationToken)
        {
            var assignments = new List<TopicPartitionOffset>();
            var targets = new Dictionary<int, long>();
            foreach (var kv in partitionStartOffsets.OrderBy(x => x.Key))
            {
                var high = partitionHighWatermarks.GetValueOrDefault(kv.Key, kv.Value);
                if (kv.Value < high)
                {
                    assignments.Add(new TopicPartitionOffset(topic, kv.Key, new Offset(kv.Value)));
                    targets[kv.Key] = high;
                }
            }

            if (assignments.Count == 0)
            {
                return Array.Empty<KafkaConsumedRecord>();
            }

            using var consumer = new ConsumerBuilder<Ignore, byte[]>(_config.ToConsumerConfig(_groupId)).Build();
            consumer.Assign(assignments);

            var records = new List<KafkaConsumedRecord>(Math.Min(maxRecords, 512));
            var deadline = Stopwatch.StartNew();
            var idlePolls = 0;

            while (records.Count < maxRecords && deadline.Elapsed < maxDuration && targets.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = consumer.Consume(TimeSpan.FromMilliseconds(250));
                if (result == null)
                {
                    if (++idlePolls >= 6) break;
                    continue;
                }

                idlePolls = 0;
                if (result.IsPartitionEOF)
                {
                    targets.Remove(result.Partition.Value);
                    continue;
                }

                var tsUtc = result.Message.Timestamp.Type != TimestampType.NotAvailable
                    ? result.Message.Timestamp.UtcDateTime
                    : DateTime.UtcNow;
                var raw = result.Message.Value;
                var textVal = raw is { Length: > 0 } && !ConfluentSchemaRegistryClient.IsConfluentWirePayload(raw)
                    ? Encoding.UTF8.GetString(raw)
                    : null;
                records.Add(new KafkaConsumedRecord(
                    result.Partition.Value,
                    result.Offset.Value,
                    textVal,
                    tsUtc,
                    raw));

                if (targets.TryGetValue(result.Partition.Value, out var targetHigh) &&
                    result.Offset.Value + 1 >= targetHigh)
                {
                    targets.Remove(result.Partition.Value);
                }
            }

            consumer.Close();
            return records;
        }

        public void Dispose()
        {
        }
    }
}
