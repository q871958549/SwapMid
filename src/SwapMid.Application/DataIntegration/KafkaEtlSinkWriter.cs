using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>Kafka 流式接收端批量写入器。</summary>
public sealed class KafkaEtlSinkWriter : IEtlSinkWriter, ITransientDependency
{
    private readonly IConfiguration _configuration;

    public KafkaEtlSinkWriter(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public DataSourceType TargetType => DataSourceType.Kafka;

    public async Task<long> WriteAsync(
        Guid pipelineId,
        Guid runId,
        DataSource target,
        EtlTransformConfig transform,
        Func<Func<EtlSourceBatch, CancellationToken, Task>, CancellationToken, Task> streamBatchesAsync,
        CancellationToken cancellationToken)
    {
        if (transform.Mode != EtlWriteMode.Append)
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid)
                .WithData("Reason", "Kafka 目标仅支持 append 写入模式。");
        }

        if (!KafkaSinkConfigContract.IsValidTopicName(transform.TargetTable))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid)
                .WithData("Reason", "Kafka 目标 Topic 名称格式无效。");
        }

        var config = KafkaSinkConfigContract.ParseForWrite(target.Config, requirePasswordIfSasl: true);
        await DataIntegrationSinkEndpointPolicy.ValidateKafkaBrokersAsync(
            config.Brokers,
            _configuration,
            cancellationToken);

        using var producer = new ProducerBuilder<string?, byte[]>(config.ToProducerConfig()).Build();

        long totalWritten = 0;
        var pipelineHeader = Encoding.UTF8.GetBytes(pipelineId.ToString("D", CultureInfo.InvariantCulture));
        var runHeader = Encoding.UTF8.GetBytes(runId.ToString("N", CultureInfo.InvariantCulture));
        var contentTypeHeader = "application/json"u8.ToArray();

        await streamBatchesAsync(async (batch, batchCt) =>
        {
            if (batch.Rows.Count == 0)
            {
                return;
            }

            var tasks = new List<Task<DeliveryResult<string?, byte[]>>>(batch.Rows.Count);
            for (var i = 0; i < batch.Rows.Count; i++)
            {
                var payload = SerializeRowJsonBytes(batch.TargetColumns, batch.Rows[i]);
                var headers = new Headers
                {
                    { "x-swapmid-pipeline-id", pipelineHeader },
                    { "x-swapmid-run-id", runHeader },
                    { "content-type", contentTypeHeader },
                };

                var message = new Message<string?, byte[]>
                {
                    Key = $"{runId:N}:{batch.BatchIndex}:{i}",
                    Value = payload,
                    Headers = headers,
                };

                tasks.Add(producer.ProduceAsync(transform.TargetTable, message, batchCt));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (ProduceException<string?, byte[]> ex)
            {
                var errorType = ex.GetType().Name;
                throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                    .WithData("Reason", $"Kafka 批量投递失败（{errorType}）。");
            }

            totalWritten += batch.Rows.Count;
        }, cancellationToken);

        return totalWritten;
    }

    internal static byte[] SerializeRowJsonBytes(string[] columns, object?[] row)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            for (var i = 0; i < columns.Length; i++)
            {
                writer.WritePropertyName(columns[i]);
                ClickHouseEtlSinkWriter.WriteJsonValue(writer, i < row.Length ? row[i] : null);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
