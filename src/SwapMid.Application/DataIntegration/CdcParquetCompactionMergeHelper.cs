using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Parquet;
using Parquet.Schema;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// Lakehouse 小文件合并压缩的文件格式流式归并辅助类（Parquet RowGroup 复制与 JSONL 拼接）。
/// </summary>
internal static class CdcParquetCompactionMergeHelper
{
    internal static async Task MergeParquetFilesAsync(
        List<CdcObjectStorageLakehouseWriter.CdcLatestDataFileEntry> candidates,
        Func<string, CancellationToken, Task<byte[]>> fetchBytesAsync,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ParquetSchema? targetSchema = null;
        DataField[]? dataFields = null;
        ParquetWriter? writer = null;

        await using var outStream = new FileStream(
            outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        try
        {
            foreach (var candidate in candidates)
            {
                var bytes = await fetchBytesAsync(candidate.DataObjectKey, cancellationToken);
                await using var mem = new MemoryStream(bytes, writable: false);
                await using var reader = await ParquetReader.CreateAsync(mem, cancellationToken: cancellationToken);
                if (targetSchema == null)
                {
                    targetSchema = reader.Schema;
                    dataFields = targetSchema.GetDataFields();
                    var fieldIdByName = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (reader.Metadata?.Schema != null)
                    {
                        foreach (var elem in reader.Metadata.Schema)
                        {
                            if (elem.FieldId.HasValue && elem.FieldId.Value > 0 && !string.IsNullOrEmpty(elem.Name))
                            {
                                fieldIdByName[elem.Name] = elem.FieldId.Value;
                            }
                        }
                    }

                    foreach (var df in dataFields)
                    {
                        if (fieldIdByName.TryGetValue(df.Name, out var fid))
                        {
                            df.FieldId = fid;
                        }
                    }

                    writer = await ParquetWriter.CreateAsync(targetSchema, outStream, cancellationToken: cancellationToken);
                }

                for (var rg = 0; rg < reader.RowGroupCount; rg++)
                {
                    using var rgReader = reader.OpenRowGroupReader(rg);
                    var rowCount = checked((int)rgReader.RowCount);
                    if (rowCount == 0) continue;
                    using var rgWriter = writer!.CreateRowGroup();
                    foreach (var field in dataFields!)
                    {
                        await CopyParquetColumnAsync(rgReader, rgWriter, field, rowCount, cancellationToken);
                    }
                }
            }

            if (writer != null)
            {
                await writer.DisposeAsync();
                writer = null;
            }
        }
        finally
        {
            if (writer != null) await writer.DisposeAsync();
        }
    }

    internal static async Task MergeJsonLinesAsync(
        List<CdcObjectStorageLakehouseWriter.CdcLatestDataFileEntry> candidates,
        Func<string, CancellationToken, Task<byte[]>> fetchBytesAsync,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using var outStream = new FileStream(
            outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        foreach (var candidate in candidates)
        {
            var bytes = await fetchBytesAsync(candidate.DataObjectKey, cancellationToken);
            await outStream.WriteAsync(bytes, cancellationToken);
            if (bytes.Length > 0 && bytes[^1] != (byte)'\n')
            {
                await outStream.WriteAsync(new[] { (byte)'\n' }, cancellationToken);
            }
        }
    }

    private static async Task CopyParquetColumnAsync(
        ParquetRowGroupReader rgReader,
        ParquetRowGroupWriter rgWriter,
        DataField field,
        int rowCount,
        CancellationToken cancellationToken)
    {
        var clrType = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        if (clrType == typeof(string) || clrType == typeof(ReadOnlyMemory<char>))
        {
            var buf = new string?[rowCount];
            if (rgReader.ColumnExists(field))
            {
                await rgReader.ReadAsync(field, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync(field, buf);
            return;
        }

        if (clrType == typeof(byte[]) || clrType == typeof(ReadOnlyMemory<byte>))
        {
            var buf = new byte[]?[rowCount];
            if (rgReader.ColumnExists(field))
            {
                await rgReader.ReadAsync(field, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync(field, buf);
            return;
        }

        if (clrType == typeof(bool)) await CopyStructColumnAsync<bool>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(byte)) await CopyStructColumnAsync<byte>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(sbyte)) await CopyStructColumnAsync<sbyte>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(short)) await CopyStructColumnAsync<short>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(ushort)) await CopyStructColumnAsync<ushort>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(int)) await CopyStructColumnAsync<int>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(uint)) await CopyStructColumnAsync<uint>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(long)) await CopyStructColumnAsync<long>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(ulong)) await CopyStructColumnAsync<ulong>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(float)) await CopyStructColumnAsync<float>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(double)) await CopyStructColumnAsync<double>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(decimal)) await CopyStructColumnAsync<decimal>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateTime)) await CopyStructColumnAsync<DateTime>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateTimeOffset)) await CopyStructColumnAsync<DateTimeOffset>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateOnly)) await CopyStructColumnAsync<DateOnly>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(TimeOnly)) await CopyStructColumnAsync<TimeOnly>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(TimeSpan)) await CopyStructColumnAsync<TimeSpan>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else if (clrType == typeof(Guid)) await CopyStructColumnAsync<Guid>(rgReader, rgWriter, field, rowCount, cancellationToken);
        else
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", $"Parquet 合并暂不支持列类型 {field.Name} ({field.ClrType.Name})。");
        }
    }

    private static async Task CopyStructColumnAsync<T>(
        ParquetRowGroupReader rgReader,
        ParquetRowGroupWriter rgWriter,
        DataField field,
        int rowCount,
        CancellationToken cancellationToken)
        where T : struct
    {
        if (field.IsNullable)
        {
            var buf = new T?[rowCount];
            if (rgReader.ColumnExists(field))
            {
                await rgReader.ReadAsync<T>(field, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync<T>(field, (ReadOnlyMemory<T?>)buf, cancellationToken: cancellationToken);
        }
        else
        {
            var buf = new T[rowCount];
            if (rgReader.ColumnExists(field))
            {
                await rgReader.ReadAsync<T>(field, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync<T>(field, (ReadOnlyMemory<T>)buf, cancellationToken: cancellationToken);
        }
    }
}
