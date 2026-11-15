using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Schema;
using SwapMid.Infrastructure;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>S3 / MinIO 对象存储（Apache Parquet RowGroup / JSONL）流式写入器。</summary>
public sealed class ObjectStorageEtlSinkWriter : IEtlSinkWriter, ITransientDependency
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public ObjectStorageEtlSinkWriter(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public DataSourceType TargetType => DataSourceType.ObjectStorage;

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
                .WithData("Reason", "对象存储目标仅支持 append 写入模式（按 RunId 幂等写入新对象）。");
        }

        if (!ObjectStorageSinkConfigContract.IsValidDestinationPath(transform.TargetTable))
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid)
                .WithData("Reason", "对象存储目标路径前缀格式无效或包含越界目录段。");
        }

        var config = ObjectStorageSinkConfigContract.ParseForWrite(target.Config, requireSecretKey: true);
        var objectKey = config.BuildObjectKey(transform.TargetTable, runId);
        var objectUri = config.BuildObjectUri(objectKey);

        await DataIntegrationSinkEndpointPolicy.ValidateHttpEndpointAsync(
            objectUri,
            _configuration,
            cancellationToken);

        var tempPath = Path.Combine(Path.GetTempPath(), $"swapmid-etl-{runId:N}.{config.Format}");
        try
        {
            var totalRows = config.Format == "jsonl"
                ? await WriteJsonLinesTempFileAsync(tempPath, streamBatchesAsync, cancellationToken)
                : await WriteParquetTempFileAsync(tempPath, streamBatchesAsync, cancellationToken);

            if (totalRows == 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
            {
                return 0;
            }

            await UploadTempFileAsync(tempPath, objectUri, config, cancellationToken);
            return totalRows;
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    internal static async Task<long> WriteParquetTempFileAsync(
        string filePath,
        Func<Func<EtlSourceBatch, CancellationToken, Task>, CancellationToken, Task> streamBatchesAsync,
        CancellationToken cancellationToken)
    {
        long totalRows = 0;
        ParquetFieldSpec[]? fieldSpecs = null;
        ParquetWriter? parquetWriter = null;

        await using var fileStream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);

        try
        {
            await streamBatchesAsync(async (batch, batchCt) =>
            {
                if (batch.Rows.Count == 0)
                {
                    return;
                }

                if (parquetWriter is null)
                {
                    fieldSpecs = BuildParquetFields(batch.TargetColumns, batch.TargetClrTypes);
                    var schema = new ParquetSchema(fieldSpecs.Select(s => s.Field).ToArray());
                    parquetWriter = await ParquetWriter.CreateAsync(
                        schema,
                        fileStream,
                        cancellationToken: batchCt);
                }

                using var rowGroupWriter = parquetWriter.CreateRowGroup();
                for (var colIdx = 0; colIdx < fieldSpecs!.Length; colIdx++)
                {
                    await WriteParquetColumnAsync(rowGroupWriter, fieldSpecs[colIdx], batch.Rows, colIdx, batchCt);
                }

                totalRows += batch.Rows.Count;
            }, cancellationToken);

            if (parquetWriter is not null)
            {
                await parquetWriter.DisposeAsync();
                parquetWriter = null;
            }
        }
        finally
        {
            if (parquetWriter is not null)
            {
                await parquetWriter.DisposeAsync();
            }
        }

        return totalRows;
    }

    private static async Task<long> WriteJsonLinesTempFileAsync(
        string filePath,
        Func<Func<EtlSourceBatch, CancellationToken, Task>, CancellationToken, Task> streamBatchesAsync,
        CancellationToken cancellationToken)
    {
        long totalRows = 0;
        await using var fileStream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);

        await streamBatchesAsync(async (batch, batchCt) =>
        {
            if (batch.Rows.Count == 0)
            {
                return;
            }

            await using var jsonStream = ClickHouseEtlSinkWriter.SerializeJsonEachRow(batch);
            await jsonStream.CopyToAsync(fileStream, batchCt);
            totalRows += batch.Rows.Count;
        }, cancellationToken);

        return totalRows;
    }

    private async Task UploadTempFileAsync(
        string filePath,
        Uri objectUri,
        ObjectStorageSinkConfig config,
        CancellationToken cancellationToken)
    {
        await using var uploadStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        var hashBytes = await SHA256.HashDataAsync(uploadStream, cancellationToken);
        var payloadHashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        uploadStream.Position = 0;

        using var content = new StreamContent(uploadStream);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            config.Format == "jsonl" ? "application/x-ndjson" : "application/vnd.apache.parquet");
        content.Headers.ContentLength = uploadStream.Length;

        using var request = new HttpRequestMessage(HttpMethod.Put, objectUri)
        {
            Content = content,
        };
        AwsSigV4Signer.Sign(
            request,
            config.Region,
            config.AccessKey,
            config.SecretKey ?? string.Empty,
            payloadHashHex);

        var client = _httpClientFactory.CreateClient(ExternalHttpClientNames.DataIntegrationSink);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData(
                    "Reason",
                    SinkConfigContractHelpers.RedactDiagnosticText(
                        $"S3/MinIO PUT 失败 ({(int)response.StatusCode}): {err}"));
        }
    }

    private static ParquetFieldSpec[] BuildParquetFields(string[] columns, Type[] clrTypes)
    {
        var specs = new ParquetFieldSpec[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            var sourceType = i < clrTypes.Length ? clrTypes[i] : typeof(string);
            var unwrapped = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
            specs[i] = unwrapped switch
            {
                _ when unwrapped == typeof(bool) => new(new DataField<bool?>(columns[i]), ParquetColumnKind.Boolean),
                _ when unwrapped == typeof(byte) => new(new DataField<byte?>(columns[i]), ParquetColumnKind.Byte),
                _ when unwrapped == typeof(short) => new(new DataField<short?>(columns[i]), ParquetColumnKind.Int16),
                _ when unwrapped == typeof(int) => new(new DataField<int?>(columns[i]), ParquetColumnKind.Int32),
                _ when unwrapped == typeof(long) => new(new DataField<long?>(columns[i]), ParquetColumnKind.Int64),
                _ when unwrapped == typeof(float) => new(new DataField<float?>(columns[i]), ParquetColumnKind.Single),
                _ when unwrapped == typeof(double) => new(new DataField<double?>(columns[i]), ParquetColumnKind.Double),
                _ when unwrapped == typeof(decimal) => new(new DataField<decimal?>(columns[i]), ParquetColumnKind.Decimal),
                _ when unwrapped == typeof(DateTime) || unwrapped == typeof(DateTimeOffset) || unwrapped == typeof(DateOnly) =>
                    new(new DataField<DateTime?>(columns[i]), ParquetColumnKind.DateTime),
                _ when unwrapped == typeof(Guid) => new(new DataField<Guid?>(columns[i]), ParquetColumnKind.Guid),
                _ when unwrapped == typeof(byte[]) => new(new DataField<byte[]?>(columns[i]), ParquetColumnKind.Binary),
                _ => new(new DataField<string?>(columns[i]), ParquetColumnKind.String),
            };
        }

        return specs;
    }

    private static async Task WriteParquetColumnAsync(
        ParquetRowGroupWriter writer,
        ParquetFieldSpec spec,
        IReadOnlyList<object?[]> rows,
        int colIndex,
        CancellationToken cancellationToken)
    {
        var field = spec.Field;
        switch (spec.Kind)
        {
            case ParquetColumnKind.Boolean:
                await writer.WriteAsync<bool>(field, MapNullable(rows, colIndex, v => Convert.ToBoolean(v, CultureInfo.InvariantCulture)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Byte:
                await writer.WriteAsync<byte>(field, MapNullable(rows, colIndex, v => Convert.ToByte(v, CultureInfo.InvariantCulture)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Int16:
                await writer.WriteAsync<short>(field, MapNullable(rows, colIndex, v => Convert.ToInt16(v, CultureInfo.InvariantCulture)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Int32:
                await writer.WriteAsync<int>(field, MapNullable(rows, colIndex, v => Convert.ToInt32(v, CultureInfo.InvariantCulture)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Int64:
                await writer.WriteAsync<long>(field, MapNullable(rows, colIndex, v => Convert.ToInt64(v, CultureInfo.InvariantCulture)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Single:
                await writer.WriteAsync<float>(field, MapNullable(rows, colIndex, v => Convert.ToSingle(v, CultureInfo.InvariantCulture)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Double:
                await writer.WriteAsync<double>(field, MapNullable(rows, colIndex, v => Convert.ToDouble(v, CultureInfo.InvariantCulture)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Decimal:
                await writer.WriteAsync<decimal>(field, MapNullable(rows, colIndex, v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Guid:
                await writer.WriteAsync<Guid>(field, MapNullable(rows, colIndex, v => v is Guid g ? g : Guid.Parse(v.ToString()!)), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.DateTime:
                await writer.WriteAsync<DateTime>(field, MapNullable(rows, colIndex, ConvertUtcDateTime), cancellationToken: cancellationToken);
                return;
            case ParquetColumnKind.Binary:
            {
                var data = new byte[rows.Count][];
                for (var r = 0; r < rows.Count; r++)
                {
                    data[r] = (colIndex < rows[r].Length ? rows[r][colIndex] as byte[] : null)!;
                }

                await writer.WriteAsync(field, data);
                return;
            }
            default:
            {
                var strings = new string[rows.Count];
                for (var r = 0; r < rows.Count; r++)
                {
                    var raw = colIndex < rows[r].Length ? rows[r][colIndex] : null;
                    strings[r] = (raw is null or DBNull
                        ? null
                        : Convert.ToString(raw, CultureInfo.InvariantCulture))!;
                }

                await writer.WriteAsync(field, strings);
                return;
            }
        }
    }

    private enum ParquetColumnKind
    {
        String,
        Boolean,
        Byte,
        Int16,
        Int32,
        Int64,
        Single,
        Double,
        Decimal,
        Guid,
        DateTime,
        Binary,
    }

    private sealed record ParquetFieldSpec(DataField Field, ParquetColumnKind Kind);

    private static T?[] MapNullable<T>(
        IReadOnlyList<object?[]> rows,
        int colIndex,
        Func<object, T> convert)
        where T : struct
    {
        var result = new T?[rows.Count];
        for (var r = 0; r < rows.Count; r++)
        {
            var raw = colIndex < rows[r].Length ? rows[r][colIndex] : null;
            if (raw is not null and not DBNull)
            {
                result[r] = convert(raw);
            }
        }

        return result;
    }

    private static DateTime ConvertUtcDateTime(object value)
    {
        if (value is DateOnly dateOnly)
        {
            return dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }

        if (value is DateTimeOffset dto)
        {
            return dto.UtcDateTime;
        }

        if (value is DateTime dt)
        {
            return dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime();
        }

        return Convert.ToDateTime(value, CultureInfo.InvariantCulture).ToUniversalTime();
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temp file cleanup errors in finally block.
        }
    }
}
