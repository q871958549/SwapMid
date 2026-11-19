using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SwapMid.DataIntegration;

/// <summary>
/// 纯 .NET 零外部依赖的 Apache Avro 1.11 Object Container File（OCF，codec="null"）读写编解码器。
/// 专为 Apache Iceberg v2 <c>manifest-list</c> 与 <c>manifest-file</c> 精确二进制互操作设计。
/// </summary>
internal static class IcebergAvroOcfCodec
{
    internal static readonly byte[] MagicBytes = [(byte)'O', (byte)'b', (byte)'j', 0x01];

    internal sealed record OcfFile(
        IReadOnlyDictionary<string, string> Metadata,
        IReadOnlyList<byte[]> Blocks,
        long TotalRecordCount);

    internal static byte[] WriteSingleBlockFile(
        IReadOnlyDictionary<string, string> metadata,
        long recordCount,
        byte[] encodedRecordsBlock,
        string syncSeed)
    {
        var syncMarker = ComputeSyncMarker(syncSeed);
        using var ms = new MemoryStream(encodedRecordsBlock.Length + 2048);

        ms.Write(MagicBytes, 0, MagicBytes.Length);

        var headerMap = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var kv in metadata)
        {
            headerMap[kv.Key] = Encoding.UTF8.GetBytes(kv.Value);
        }

        if (!headerMap.ContainsKey("avro.codec"))
        {
            headerMap["avro.codec"] = Encoding.UTF8.GetBytes("null");
        }

        WriteMap(ms, headerMap);
        ms.Write(syncMarker, 0, syncMarker.Length);

        if (recordCount > 0 && encodedRecordsBlock.Length > 0)
        {
            WriteLong(ms, recordCount);
            WriteLong(ms, encodedRecordsBlock.Length);
            ms.Write(encodedRecordsBlock, 0, encodedRecordsBlock.Length);
            ms.Write(syncMarker, 0, syncMarker.Length);
        }

        return ms.ToArray();
    }

    internal static OcfFile ReadFile(byte[] rawBytes)
    {
        if (rawBytes.Length < 20 ||
            rawBytes[0] != MagicBytes[0] ||
            rawBytes[1] != MagicBytes[1] ||
            rawBytes[2] != MagicBytes[2] ||
            rawBytes[3] != MagicBytes[3])
        {
            throw new InvalidDataException("Invalid Apache Avro OCF magic header.");
        }

        using var ms = new MemoryStream(rawBytes, writable: false);
        ms.Position = 4;

        var rawMeta = ReadMap(ms);
        var metadata = new Dictionary<string, string>(rawMeta.Count, StringComparer.Ordinal);
        foreach (var kv in rawMeta)
        {
            metadata[kv.Key] = Encoding.UTF8.GetString(kv.Value);
        }

        if (metadata.TryGetValue("avro.codec", out var codec) &&
            !string.Equals(codec, "null", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported Avro OCF codec '{codec}'.");
        }

        var syncMarker = ReadExact(ms, 16);
        var blocks = new List<byte[]>();
        long totalRecords = 0;

        while (ms.Position < ms.Length)
        {
            var count = ReadLong(ms);
            var byteLen = ReadLong(ms);
            if (count < 0 || byteLen < 0 || byteLen > ms.Length - ms.Position)
            {
                throw new InvalidDataException("Corrupt Avro OCF block header.");
            }

            var blockData = ReadExact(ms, checked((int)byteLen));
            var trailingSync = ReadExact(ms, 16);
            if (!syncMarker.AsSpan().SequenceEqual(trailingSync))
            {
                throw new InvalidDataException("Avro OCF block sync marker mismatch.");
            }

            blocks.Add(blockData);
            totalRecords += count;
        }

        return new OcfFile(metadata, blocks, totalRecords);
    }

    internal static void WriteInt(Stream s, int value)
        => WriteVarUInt64(s, (ulong)(uint)((value << 1) ^ (value >> 31)));

    internal static void WriteLong(Stream s, long value)
        => WriteVarUInt64(s, (ulong)((value << 1) ^ (value >> 63)));

    internal static void WriteBool(Stream s, bool value)
        => s.WriteByte(value ? (byte)1 : (byte)0);

    internal static void WriteString(Stream s, string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteLong(s, utf8.Length);
        s.Write(utf8, 0, utf8.Length);
    }

    internal static void WriteBytes(Stream s, byte[] value)
    {
        WriteLong(s, value.Length);
        s.Write(value, 0, value.Length);
    }

    internal static void WriteNullableLong(Stream s, long? value)
    {
        if (!value.HasValue)
        {
            WriteLong(s, 0);
            return;
        }

        WriteLong(s, 1);
        WriteLong(s, value.Value);
    }

    internal static void WriteNullableInt(Stream s, int? value)
    {
        if (!value.HasValue)
        {
            WriteLong(s, 0);
            return;
        }

        WriteLong(s, 1);
        WriteInt(s, value.Value);
    }

    internal static int ReadInt(Stream s)
    {
        var u = ReadVarUInt64(s);
        return (int)((u >> 1) ^ (ulong)-(long)(u & 1));
    }

    internal static long ReadLong(Stream s)
    {
        var u = ReadVarUInt64(s);
        return (long)((u >> 1) ^ (ulong)-(long)(u & 1));
    }

    internal static bool ReadBool(Stream s)
    {
        var b = s.ReadByte();
        if (b < 0) throw new EndOfStreamException("Unexpected EOF reading Avro bool.");
        return b != 0;
    }

    internal static string ReadString(Stream s)
    {
        var bytes = ReadBytes(s);
        return Encoding.UTF8.GetString(bytes);
    }

    internal static byte[] ReadBytes(Stream s)
    {
        var len = ReadLong(s);
        if (len < 0 || len > int.MaxValue)
        {
            throw new InvalidDataException("Invalid Avro bytes length.");
        }

        return len == 0 ? Array.Empty<byte>() : ReadExact(s, (int)len);
    }

    internal static long? ReadNullableLong(Stream s)
        => ReadLong(s) == 0 ? null : ReadLong(s);

    internal static int? ReadNullableInt(Stream s)
        => ReadLong(s) == 0 ? null : ReadInt(s);

    private static void WriteMap(Stream s, IReadOnlyDictionary<string, byte[]> map)
    {
        if (map.Count > 0)
        {
            WriteLong(s, map.Count);
            foreach (var kv in map)
            {
                WriteString(s, kv.Key);
                WriteBytes(s, kv.Value);
            }
        }

        WriteLong(s, 0);
    }

    private static Dictionary<string, byte[]> ReadMap(Stream s)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        while (true)
        {
            var count = ReadLong(s);
            if (count == 0)
            {
                break;
            }

            if (count < 0)
            {
                count = -count;
                _ = ReadLong(s); // byte-size prefix for negative block count
            }

            for (long i = 0; i < count; i++)
            {
                var key = ReadString(s);
                var val = ReadBytes(s);
                result[key] = val;
            }
        }

        return result;
    }

    private static void WriteVarUInt64(Stream s, ulong value)
    {
        while ((value & ~0x7FUL) != 0)
        {
            s.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        s.WriteByte((byte)value);
    }

    private static ulong ReadVarUInt64(Stream s)
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var b = s.ReadByte();
            if (b < 0) throw new EndOfStreamException("Unexpected EOF reading Avro varint.");
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift > 63) throw new InvalidDataException("Avro varint overflow.");
        }
    }

    private static byte[] ReadExact(Stream s, int count)
    {
        var buf = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = s.Read(buf, offset, count - offset);
            if (read == 0) throw new EndOfStreamException("Unexpected EOF in Avro stream.");
            offset += read;
        }

        return buf;
    }

    private static byte[] ComputeSyncMarker(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return hash[..16];
    }
}
