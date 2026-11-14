using System;
using System.Buffers;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.DataIntegration;

/// <summary>
/// 源 reader 的类型与载荷边界。未知 CLR/provider 类型一律 fail-closed，避免 UDT、
/// XML、空间/向量等对象先被驱动物化后再被错误地按 32 字节估算。
/// </summary>
internal static class EtlReaderBoundary
{
    private const int StreamBufferSize = 16 * 1024;

    internal static void ValidatePreReadSchema(
        DbDataReader reader,
        RelationalDataSourceConnection.Probe sourceProbe)
    {
        long declaredRowBytes = 0;
        var schema = reader.GetColumnSchema();
        if (schema.Count != reader.FieldCount)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            var fieldType = reader.GetFieldType(ordinal);
            var typeName = reader.GetDataTypeName(ordinal);
            var isProviderNull = IsKnownProviderNull(
                fieldType,
                typeName,
                sourceProbe);
            if (!isProviderNull && !IsSupportedClrType(fieldType))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            if (sourceProbe.NormalizedProvider is "sqlserver" or "mssql" &&
                IsUnsupportedSqlServerType(typeName))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            if (sourceProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql" &&
                IsUnsupportedPostgreSqlType(typeName))
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            if (sourceProbe.NormalizedProvider is not ("mysql" or "mariadb"))
            {
                continue;
            }

            var declaredBytes = GetMySqlDeclaredBytes(
                fieldType,
                schema[ordinal].ColumnSize,
                isProviderNull);
            if (declaredBytes > EtlPipelineRunner.MaxCellPayloadBytes)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            declaredRowBytes = checked(declaredRowBytes + declaredBytes);
            if (declaredRowBytes > EtlPipelineRunner.MaxRowPayloadBytes)
            {
                // MySqlConnector 在 ReadAsync 返回 true 前会缓冲整行，因此必须用
                // schema 声明宽度把风险挡在第一次 ReadAsync 之前。
                throw EtlPipelineRunner.InvalidInput();
            }
        }
    }

    internal static async Task<Row> ReadRowAsync(
        DbDataReader reader,
        int columnCount,
        CancellationToken ct)
    {
        var values = new object?[columnCount];
        long payloadBytes = 0;
        for (var ordinal = 0; ordinal < columnCount; ordinal++)
        {
            var value = await ReadValueAsync(reader, ordinal, ct);
            values[ordinal] = value.Value;
            payloadBytes += value.PayloadBytes;
            if (payloadBytes > EtlPipelineRunner.MaxRowPayloadBytes)
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }

        return new Row(values, payloadBytes);
    }

    private static async Task<Cell> ReadValueAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken ct)
    {
        if (await reader.IsDBNullAsync(ordinal, ct))
        {
            return new Cell(null, 1);
        }

        var fieldType = reader.GetFieldType(ordinal);
        if (fieldType == typeof(string))
        {
            using var textReader = reader.GetTextReader(ordinal);
            var text = await ReadTextAsync(textReader, ct);
            return new Cell(text, checked((long)text.Length * sizeof(char)));
        }

        if (fieldType == typeof(byte[]))
        {
            await using var stream = reader.GetStream(ordinal);
            var bytes = await ReadBytesAsync(stream, ct);
            return new Cell(bytes, bytes.LongLength);
        }

        if (!IsSupportedClrType(fieldType))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var value = reader.GetValue(ordinal);
        return new Cell(value, EstimateScalarBytes(value));
    }

    private static async Task<string> ReadTextAsync(
        TextReader reader,
        CancellationToken ct)
    {
        var buffer = ArrayPool<char>.Shared.Rent(StreamBufferSize);
        try
        {
            var builder = new StringBuilder();
            var totalBytes = 0L;
            int read;
            while ((read = await reader.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       ct)) > 0)
            {
                totalBytes += checked((long)read * sizeof(char));
                if (totalBytes > EtlPipelineRunner.MaxCellPayloadBytes)
                {
                    throw EtlPipelineRunner.InvalidInput();
                }

                builder.Append(buffer, 0, read);
            }

            return builder.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static async Task<byte[]> ReadBytesAsync(
        Stream stream,
        CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
        try
        {
            using var output = new MemoryStream();
            int read;
            while ((read = await stream.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       ct)) > 0)
            {
                if (output.Length + read > EtlPipelineRunner.MaxCellPayloadBytes)
                {
                    throw EtlPipelineRunner.InvalidInput();
                }

                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsSupportedClrType(Type type) =>
        type == typeof(string) ||
        type == typeof(byte[]) ||
        type == typeof(bool) ||
        type == typeof(byte) ||
        type == typeof(sbyte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long) ||
        type == typeof(ulong) ||
        type == typeof(float) ||
        type == typeof(double) ||
        type == typeof(decimal) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(DateOnly) ||
        type == typeof(TimeOnly) ||
        type == typeof(TimeSpan);

    private static bool IsUnsupportedSqlServerType(string typeName) =>
        typeName.Trim().ToLowerInvariant() is
            "xml" or
            "sql_variant" or
            "hierarchyid" or
            "geometry" or
            "geography" or
            "vector" or
            "udt";

    private static bool IsUnsupportedPostgreSqlType(string typeName)
    {
        var normalized = typeName.Trim().ToLowerInvariant();
        if (normalized.EndsWith("[]", StringComparison.Ordinal) ||
            normalized.StartsWith('_'))
        {
            return true;
        }

        return normalized is
            "xml" or
            "json" or
            "jsonb" or
            "money" or
            "bit" or
            "varbit" or
            "bit varying" or
            "cidr" or
            "inet" or
            "macaddr" or
            "macaddr8" or
            "tsvector" or
            "tsquery" or
            "point" or
            "line" or
            "lseg" or
            "box" or
            "path" or
            "polygon" or
            "circle" or
            "pg_lsn" or
            "txid_snapshot" or
            "pg_snapshot" or
            "refcursor" or
            "oidvector" or
            "int2vector";
    }

    internal static bool IsKnownProviderNull(
        Type type,
        string typeName,
        RelationalDataSourceConnection.Probe sourceProbe) =>
        (type == typeof(object) &&
         sourceProbe.NormalizedProvider is "mysql" or "mariadb" &&
         string.Equals(typeName, "NULL", StringComparison.OrdinalIgnoreCase)) ||
        (sourceProbe.NormalizedProvider is "postgresql" or "postgres" or "npgsql" &&
         string.Equals(typeName, "unknown", StringComparison.OrdinalIgnoreCase));

    private static long GetMySqlDeclaredBytes(
        Type type,
        int? columnSize,
        bool isProviderNull)
    {
        if (isProviderNull)
        {
            return 1;
        }

        if (type != typeof(string) && type != typeof(byte[]))
        {
            return 32;
        }

        if (columnSize is null or < 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return type == typeof(string)
            ? checked((long)columnSize.Value * 4)
            : columnSize.Value;
    }

    private static long EstimateScalarBytes(object value) =>
        value switch
        {
            bool or byte or sbyte => 1,
            short or ushort or char => 2,
            int or uint or float or DateOnly => 4,
            long or ulong or double or DateTime or TimeOnly or TimeSpan => 8,
            decimal or Guid or DateTimeOffset => 16,
            _ => throw EtlPipelineRunner.InvalidInput(),
        };

    internal sealed record Row(object?[] Values, long PayloadBytes);
    private sealed record Cell(object? Value, long PayloadBytes);
}
