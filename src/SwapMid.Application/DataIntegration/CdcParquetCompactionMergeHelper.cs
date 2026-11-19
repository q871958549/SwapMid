using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        if (candidates.Count == 0) return;
        var cachedBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        async Task<byte[]> GetBytesAsync(string key)
        {
            if (cachedBytes.TryGetValue(key, out var hit)) return hit;
            var fetched = await fetchBytesAsync(key, cancellationToken);
            cachedBytes[key] = fetched;
            return fetched;
        }

        // Select the newest candidate's schema so evolved promoted columns & renames are preserved across compaction.
        var (targetSchema, dataFields, _) = await ReadSchemaWithFieldIdsAsync(await GetBytesAsync(candidates[^1].DataObjectKey), cancellationToken);
        ParquetWriter? writer = null;

        await using var outStream = new FileStream(
            outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        try
        {
            writer = await ParquetWriter.CreateAsync(targetSchema, outStream, cancellationToken: cancellationToken);
            foreach (var candidate in candidates)
            {
                var bytes = await GetBytesAsync(candidate.DataObjectKey);
                await using var mem = new MemoryStream(bytes, writable: false);
                await using var reader = await ParquetReader.CreateAsync(mem, cancellationToken: cancellationToken);
                var sourceByFieldId = BuildFieldLookupByFieldId(reader);

                for (var rg = 0; rg < reader.RowGroupCount; rg++)
                {
                    using var rgReader = reader.OpenRowGroupReader(rg);
                    var rowCount = checked((int)rgReader.RowCount);
                    if (rowCount == 0) continue;
                    using var rgWriter = writer.CreateRowGroup();
                    foreach (var field in dataFields)
                    {
                        var sourceField = ResolveSourceField(rgReader, field, sourceByFieldId);
                        await CopyParquetColumnAsync(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
                    }
                }
            }

            await writer.DisposeAsync();
            writer = null;
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
        DataField? sourceField,
        DataField field,
        int rowCount,
        CancellationToken cancellationToken)
    {
        var clrType = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        if (clrType == typeof(string) || clrType == typeof(ReadOnlyMemory<char>))
        {
            var buf = new string?[rowCount];
            if (sourceField != null)
            {
                await rgReader.ReadAsync(sourceField, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync(field, buf);
            return;
        }

        if (clrType == typeof(byte[]) || clrType == typeof(ReadOnlyMemory<byte>))
        {
            var buf = new byte[]?[rowCount];
            if (sourceField != null)
            {
                await rgReader.ReadAsync(sourceField, buf.AsMemory(), cancellationToken: cancellationToken);
            }
            await rgWriter.WriteAsync(field, buf);
            return;
        }

        if (clrType == typeof(bool)) await CopyStructColumnAsync<bool>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(byte)) await CopyStructColumnAsync<byte>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(sbyte)) await CopyStructColumnAsync<sbyte>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(short)) await CopyStructColumnAsync<short>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(ushort)) await CopyStructColumnAsync<ushort>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(int)) await CopyStructColumnAsync<int>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(uint)) await CopyStructColumnAsync<uint>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(long)) await CopyStructColumnAsync<long>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(ulong)) await CopyStructColumnAsync<ulong>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(float)) await CopyStructColumnAsync<float>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(double)) await CopyStructColumnAsync<double>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(decimal)) await CopyStructColumnAsync<decimal>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateTime)) await CopyStructColumnAsync<DateTime>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateTimeOffset)) await CopyStructColumnAsync<DateTimeOffset>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(DateOnly)) await CopyStructColumnAsync<DateOnly>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(TimeOnly)) await CopyStructColumnAsync<TimeOnly>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(TimeSpan)) await CopyStructColumnAsync<TimeSpan>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else if (clrType == typeof(Guid)) await CopyStructColumnAsync<Guid>(rgReader, rgWriter, sourceField, field, rowCount, cancellationToken);
        else
        {
            throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
                .WithData("Reason", $"Parquet 合并暂不支持列类型 {field.Name} ({field.ClrType.Name})。");
        }
    }

    internal static async Task<long> MergeParquetFilesWithEqualityDeletesAsync(
        List<CdcObjectStorageLakehouseWriter.CdcLatestDataFileEntry> candidates,
        IReadOnlyDictionary<string, long> dataSeqByKey,
        IReadOnlyDictionary<string, long> deletedKeyMaxSeq,
        Func<string, CancellationToken, Task<byte[]>> fetchBytesAsync,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (deletedKeyMaxSeq.Count == 0)
        {
            await MergeParquetFilesAsync(candidates, fetchBytesAsync, outputPath, cancellationToken);
            return candidates.Sum(c => c.RowCount);
        }

        if (candidates.Count == 0) return 0;
        var cachedBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        async Task<byte[]> GetBytesAsync(string key)
        {
            if (cachedBytes.TryGetValue(key, out var hit)) return hit;
            var fetched = await fetchBytesAsync(key, cancellationToken);
            cachedBytes[key] = fetched;
            return fetched;
        }

        var (targetSchema, dataFields, businessKeyField) = await ReadSchemaWithFieldIdsAsync(await GetBytesAsync(candidates[^1].DataObjectKey), cancellationToken);
        ParquetWriter? writer = null;
        long keptTotal = 0;

        await using var outStream = new FileStream(
            outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        try
        {
            writer = await ParquetWriter.CreateAsync(targetSchema, outStream, cancellationToken: cancellationToken);
            foreach (var candidate in candidates)
            {
                var dataSeq = dataSeqByKey.TryGetValue(candidate.DataObjectKey, out var seq) ? seq : 0L;
                var bytes = await GetBytesAsync(candidate.DataObjectKey);
                await using var mem = new MemoryStream(bytes, writable: false);
                await using var reader = await ParquetReader.CreateAsync(mem, cancellationToken: cancellationToken);
                var sourceByFieldId = BuildFieldLookupByFieldId(reader);

                for (var rg = 0; rg < reader.RowGroupCount; rg++)
                {
                    using var rgReader = reader.OpenRowGroupReader(rg);
                    var rowCount = checked((int)rgReader.RowCount);
                    if (rowCount == 0) continue;

                    var keepIndices = new List<int>(rowCount);
                    if (businessKeyField != null && rgReader.ColumnExists(businessKeyField))
                    {
                        var keyBuf = new string?[rowCount];
                        await rgReader.ReadAsync(businessKeyField, keyBuf.AsMemory(), cancellationToken: cancellationToken);
                        for (var i = 0; i < rowCount; i++)
                        {
                            var key = keyBuf[i];
                            if (!string.IsNullOrEmpty(key) &&
                                deletedKeyMaxSeq.TryGetValue(key, out var delSeq) &&
                                delSeq > dataSeq)
                            {
                                continue;
                            }

                            keepIndices.Add(i);
                        }
                    }
                    else
                    {
                        for (var i = 0; i < rowCount; i++) keepIndices.Add(i);
                    }

                    if (keepIndices.Count == 0) continue;
                    keptTotal += keepIndices.Count;
                    using var rgWriter = writer.CreateRowGroup();
                    foreach (var field in dataFields)
                    {
                        var sourceField = ResolveSourceField(rgReader, field, sourceByFieldId);
                        await CopyFilteredColumnAsync(rgReader, rgWriter, sourceField, field, rowCount, keepIndices, cancellationToken);
                    }
                }
            }

            await writer.DisposeAsync();
            writer = null;
        }
        finally
        {
            if (writer != null) await writer.DisposeAsync();
        }

        return keptTotal;
    }

    private static async Task CopyFilteredColumnAsync(
        ParquetRowGroupReader rgReader,
        ParquetRowGroupWriter rgWriter,
        DataField? sourceField,
        DataField field,
        int rowCount,
        List<int> keepIndices,
        CancellationToken cancellationToken)
    {
        var clrType = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        if (clrType == typeof(string) || clrType == typeof(ReadOnlyMemory<char>))
        {
            var src = new string?[rowCount];
            if (sourceField != null) await rgReader.ReadAsync(sourceField, src.AsMemory(), cancellationToken: cancellationToken);
            var dst = new string?[keepIndices.Count];
            for (var i = 0; i < keepIndices.Count; i++) dst[i] = src[keepIndices[i]];
            await rgWriter.WriteAsync(field, dst);
            return;
        }

        if (clrType == typeof(DateTime))
        {
            await CopyFilteredStructColumnAsync<DateTime>(rgReader, rgWriter, sourceField, field, rowCount, keepIndices, cancellationToken);
            return;
        }

        if (clrType == typeof(long)) { await CopyFilteredStructColumnAsync<long>(rgReader, rgWriter, sourceField, field, rowCount, keepIndices, cancellationToken); return; }
        if (clrType == typeof(int)) { await CopyFilteredStructColumnAsync<int>(rgReader, rgWriter, sourceField, field, rowCount, keepIndices, cancellationToken); return; }
        if (clrType == typeof(double)) { await CopyFilteredStructColumnAsync<double>(rgReader, rgWriter, sourceField, field, rowCount, keepIndices, cancellationToken); return; }
        if (clrType == typeof(decimal)) { await CopyFilteredStructColumnAsync<decimal>(rgReader, rgWriter, sourceField, field, rowCount, keepIndices, cancellationToken); return; }
        if (clrType == typeof(bool)) { await CopyFilteredStructColumnAsync<bool>(rgReader, rgWriter, sourceField, field, rowCount, keepIndices, cancellationToken); return; }
        if (clrType == typeof(Guid)) { await CopyFilteredStructColumnAsync<Guid>(rgReader, rgWriter, sourceField, field, rowCount, keepIndices, cancellationToken); return; }
        if (clrType == typeof(byte[]) || clrType == typeof(ReadOnlyMemory<byte>))
        {
            var src = new byte[]?[rowCount];
            if (sourceField != null) await rgReader.ReadAsync(sourceField, src.AsMemory(), cancellationToken: cancellationToken);
            var dst = new byte[]?[keepIndices.Count];
            for (var i = 0; i < keepIndices.Count; i++) dst[i] = src[keepIndices[i]];
            await rgWriter.WriteAsync(field, dst);
            return;
        }

        throw new BusinessException(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionFailed)
            .WithData("Reason", $"Parquet Merge-on-Read 压缩暂不支持列类型 {field.Name} ({field.ClrType.Name})。");
    }

    private static async Task CopyFilteredStructColumnAsync<T>(
        ParquetRowGroupReader rgReader,
        ParquetRowGroupWriter rgWriter,
        DataField? sourceField,
        DataField field,
        int rowCount,
        List<int> keepIndices,
        CancellationToken cancellationToken)
        where T : struct
    {
        if (field.IsNullable)
        {
            var src = new T?[rowCount];
            if (sourceField != null) await rgReader.ReadAsync<T>(sourceField, src.AsMemory(), cancellationToken: cancellationToken);
            var dst = new T?[keepIndices.Count];
            for (var i = 0; i < keepIndices.Count; i++) dst[i] = src[keepIndices[i]];
            await rgWriter.WriteAsync<T>(field, (ReadOnlyMemory<T?>)dst, cancellationToken: cancellationToken);
        }
        else
        {
            var src = new T[rowCount];
            if (sourceField != null) await rgReader.ReadAsync<T>(sourceField, src.AsMemory(), cancellationToken: cancellationToken);
            var dst = new T[keepIndices.Count];
            for (var i = 0; i < keepIndices.Count; i++) dst[i] = src[keepIndices[i]];
            await rgWriter.WriteAsync<T>(field, (ReadOnlyMemory<T>)dst, cancellationToken: cancellationToken);
        }
    }

    private static async Task CopyStructColumnAsync<T>(
        ParquetRowGroupReader rgReader,
        ParquetRowGroupWriter rgWriter,
        DataField? sourceField,
        DataField field,
        int rowCount,
        CancellationToken cancellationToken)
        where T : struct
    {
        if (field.IsNullable)
        {
            var buf = new T?[rowCount];
            if (sourceField != null) await rgReader.ReadAsync<T>(sourceField, buf.AsMemory(), cancellationToken: cancellationToken);
            await rgWriter.WriteAsync<T>(field, (ReadOnlyMemory<T?>)buf, cancellationToken: cancellationToken);
        }
        else
        {
            var buf = new T[rowCount];
            if (sourceField != null) await rgReader.ReadAsync<T>(sourceField, buf.AsMemory(), cancellationToken: cancellationToken);
            await rgWriter.WriteAsync<T>(field, (ReadOnlyMemory<T>)buf, cancellationToken: cancellationToken);
        }
    }

    private static async Task<(ParquetSchema Schema, DataField[] Fields, DataField? BusinessKeyField)>
        ReadSchemaWithFieldIdsAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await using var mem = new MemoryStream(bytes, writable: false);
        await using var reader = await ParquetReader.CreateAsync(mem, cancellationToken: cancellationToken);
        var schema = reader.Schema;
        var dataFields = schema.GetDataFields();
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

        DataField? bk = null;
        foreach (var df in dataFields)
        {
            if (fieldIdByName.TryGetValue(df.Name, out var fid)) df.FieldId = fid;
            if (string.Equals(df.Name, "business_key", StringComparison.OrdinalIgnoreCase)) bk = df;
        }

        return (schema, dataFields, bk);
    }

    private static Dictionary<int, DataField> BuildFieldLookupByFieldId(ParquetReader reader)
    {
        var map = new Dictionary<int, DataField>();
        var fieldsByName = reader.Schema.GetDataFields().ToDictionary(f => f.Name, StringComparer.Ordinal);
        if (reader.Metadata?.Schema == null) return map;
        foreach (var elem in reader.Metadata.Schema)
        {
            if (elem.FieldId.HasValue && elem.FieldId.Value > 0 &&
                !string.IsNullOrEmpty(elem.Name) &&
                fieldsByName.TryGetValue(elem.Name, out var df))
            {
                map[elem.FieldId.Value] = df;
            }
        }

        return map;
    }

    private static DataField? ResolveSourceField(
        ParquetRowGroupReader rgReader,
        DataField targetField,
        Dictionary<int, DataField> sourceByFieldId)
    {
        if (rgReader.ColumnExists(targetField)) return targetField;
        if (targetField.FieldId > 0 &&
            sourceByFieldId.TryGetValue(targetField.FieldId, out var matched) &&
            (Nullable.GetUnderlyingType(matched.ClrType) ?? matched.ClrType) == (Nullable.GetUnderlyingType(targetField.ClrType) ?? targetField.ClrType) &&
            rgReader.ColumnExists(matched))
        {
            return matched;
        }

        return null;
    }
}
