using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SwapMid.Infrastructure;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>ClickHouse HTTP (JSONEachRow) 流式接收端写入器。</summary>
public sealed class ClickHouseEtlSinkWriter : IEtlSinkWriter, ITransientDependency
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public ClickHouseEtlSinkWriter(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public DataSourceType TargetType => DataSourceType.ClickHouse;

    public async Task<long> WriteAsync(
        Guid pipelineId,
        Guid runId,
        DataSource target,
        EtlTransformConfig transform,
        Func<Func<EtlSourceBatch, CancellationToken, Task>, CancellationToken, Task> streamBatchesAsync,
        CancellationToken cancellationToken)
    {
        if (transform.Mode is not (EtlWriteMode.Append or EtlWriteMode.Truncate))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid)
                .WithData("Reason", "ClickHouse 目标仅支持 append 或 truncate 写入模式。");
        }

        if (!EtlTransformConfig.IsValidTargetTable(transform.TargetTable))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid)
                .WithData("Reason", "ClickHouse 目标表名必须是合法的 database.table 或 table 标识符。");
        }

        var config = ClickHouseSinkConfigContract.ParseForWrite(target.Config);
        var baseUri = config.BuildBaseUri();
        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            baseUri,
            _configuration,
            cancellationToken);

        var (dbName, tableName) = DataIntegrationSinkProbeService.SplitClickHouseTable(
            config.Database,
            transform.TargetTable);
        var quotedTable = $"{QuoteIdentifier(dbName)}.{QuoteIdentifier(tableName)}";
        var queryUri = new Uri(baseUri, $"?database={Uri.EscapeDataString(config.Database)}");
        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);

        if (transform.Mode == EtlWriteMode.Truncate)
        {
            await SendClickHouseSqlAsync(
                client,
                queryUri,
                config,
                $"TRUNCATE TABLE IF EXISTS {quotedTable}",
                cancellationToken);
        }

        long totalWritten = 0;
        await streamBatchesAsync(async (batch, batchCt) =>
        {
            if (batch.Rows.Count == 0)
            {
                return;
            }

            await using var payloadStream = SerializeJsonEachRow(batch);
            var insertPrefix = $"INSERT INTO {quotedTable} FORMAT JSONEachRow\n";
            var insertBytes = System.Text.Encoding.UTF8.GetBytes(insertPrefix);
            using var combined = new MemoryStream(checked(insertBytes.Length + (int)payloadStream.Length));
            await combined.WriteAsync(insertBytes, batchCt);
            payloadStream.Position = 0;
            await payloadStream.CopyToAsync(combined, batchCt);
            combined.Position = 0;

            using var request = new HttpRequestMessage(HttpMethod.Post, queryUri)
            {
                Content = new StreamContent(combined),
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-ndjson");
            AttachClickHouseAuth(request, config);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, batchCt);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(batchCt);
                throw ExecutionFailed($"ClickHouse HTTP {(int)response.StatusCode}: {Truncate(err)}");
            }

            totalWritten += batch.Rows.Count;
        }, cancellationToken);

        return totalWritten;
    }

    internal static MemoryStream SerializeJsonEachRow(EtlSourceBatch batch)
    {
        var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            foreach (var row in batch.Rows)
            {
                writer.WriteStartObject();
                for (var i = 0; i < batch.TargetColumns.Length; i++)
                {
                    writer.WritePropertyName(batch.TargetColumns[i]);
                    WriteJsonValue(writer, i < row.Length ? row[i] : null);
                }

                writer.WriteEndObject();
                writer.Flush();
                stream.WriteByte((byte)'\n');
                writer.Reset();
            }
        }

        stream.Position = 0;
        return stream;
    }

    internal static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case byte u8:
                writer.WriteNumberValue(u8);
                break;
            case short i16:
                writer.WriteNumberValue(i16);
                break;
            case int i32:
                writer.WriteNumberValue(i32);
                break;
            case long i64:
                writer.WriteNumberValue(i64);
                break;
            case float f32:
                writer.WriteNumberValue(f32);
                break;
            case double f64:
                writer.WriteNumberValue(f64);
                break;
            case decimal dec:
                writer.WriteNumberValue(dec);
                break;
            case Guid g:
                writer.WriteStringValue(g.ToString("D", CultureInfo.InvariantCulture));
                break;
            case DateOnly d:
                writer.WriteStringValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case TimeOnly tm:
                writer.WriteStringValue(tm.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                writer.WriteStringValue(
                    (dt.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                        : dt.ToUniversalTime()).ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static async Task SendClickHouseSqlAsync(
        HttpClient client,
        Uri queryUri,
        ClickHouseSinkConfig config,
        string sql,
        CancellationToken cancellationToken)
    {
        using var request = DataIntegrationSinkProbeService.CreateClickHousePostRequest(queryUri, config, sql);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw ExecutionFailed($"ClickHouse HTTP {(int)response.StatusCode}: {Truncate(err)}");
        }
    }

    private static void AttachClickHouseAuth(HttpRequestMessage request, ClickHouseSinkConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Username))
        {
            request.Headers.TryAddWithoutValidation("X-ClickHouse-User", config.Username);
        }

        if (!string.IsNullOrEmpty(config.Password))
        {
            request.Headers.TryAddWithoutValidation("X-ClickHouse-Key", config.Password);
        }
    }

    private static string QuoteIdentifier(string identifier)
        => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    private static BusinessException ExecutionFailed(string reason)
        => new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", SinkConfigContractHelpers.RedactDiagnosticText(reason));

    private static string Truncate(string message)
    {
        var clean = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 240 ? clean : clean[..240];
    }
}
