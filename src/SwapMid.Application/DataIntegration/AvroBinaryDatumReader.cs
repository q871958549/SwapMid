using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SwapMid.DataIntegration;

/// <summary>
/// 纯 .NET 零依赖 Apache Avro 1.11 二进制 Datum 解码器：
/// 解析 Confluent / Redpanda Schema Registry 返回的 Avro JSON Schema，
/// 将二进制 Avro 载荷（支持 record/union/array/map/enum/fixed/logicalType 与命名类型引用）解码为标准对象树。
/// </summary>
internal static class AvroBinaryDatumReader
{
    private const int MaxNestingDepth = 32;
    private const int MaxCollectionItems = 10_000;

    internal static object? Decode(string avroSchemaJson, ReadOnlySpan<byte> payload)
    {
        using var doc = JsonDocument.Parse(avroSchemaJson);
        var namedTypes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        CollectNamedTypes(doc.RootElement, null, namedTypes);

        using var ms = new MemoryStream(payload.ToArray(), writable: false);
        return ReadValue(ms, doc.RootElement, null, namedTypes, depth: 0);
    }

    private static void CollectNamedTypes(
        JsonElement schema,
        string? enclosingNamespace,
        Dictionary<string, JsonElement> namedTypes)
    {
        if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var branch in schema.EnumerateArray())
            {
                CollectNamedTypes(branch, enclosingNamespace, namedTypes);
            }

            return;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var typeProp = schema.TryGetProperty("type", out var t) ? t : default;
        if (typeProp.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var typeName = typeProp.GetString();
        if (typeName is "record" or "enum" or "fixed")
        {
            var ns = schema.TryGetProperty("namespace", out var nsProp) && nsProp.ValueKind == JsonValueKind.String
                ? nsProp.GetString()
                : enclosingNamespace;
            if (schema.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            {
                var rawName = nameProp.GetString()!;
                var fullName = rawName.Contains('.') || string.IsNullOrWhiteSpace(ns) ? rawName : $"{ns}.{rawName}";
                namedTypes[fullName] = schema;
                namedTypes[rawName] = schema;
            }

            if (typeName == "record" &&
                schema.TryGetProperty("fields", out var fieldsProp) &&
                fieldsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var field in fieldsProp.EnumerateArray())
                {
                    if (field.TryGetProperty("type", out var ft))
                    {
                        CollectNamedTypes(ft, ns, namedTypes);
                    }
                }
            }
        }
        else if (typeName == "array" && schema.TryGetProperty("items", out var items))
        {
            CollectNamedTypes(items, enclosingNamespace, namedTypes);
        }
        else if (typeName == "map" && schema.TryGetProperty("values", out var values))
        {
            CollectNamedTypes(values, enclosingNamespace, namedTypes);
        }
    }

    private static object? ReadValue(
        Stream ms,
        JsonElement schema,
        string? enclosingNamespace,
        Dictionary<string, JsonElement> namedTypes,
        int depth)
    {
        if (depth > MaxNestingDepth)
        {
            throw new InvalidDataException("Avro schema nesting depth exceeded safety limit.");
        }

        if (schema.ValueKind == JsonValueKind.String)
        {
            var prim = schema.GetString()!;
            return ReadPrimitiveOrNamed(ms, prim, enclosingNamespace, namedTypes, depth);
        }

        if (schema.ValueKind == JsonValueKind.Array)
        {
            var branchIndex = IcebergAvroOcfCodec.ReadInt(ms);
            if (branchIndex < 0 || branchIndex >= schema.GetArrayLength())
            {
                throw new InvalidDataException($"Avro union branch index {branchIndex} out of range.");
            }

            return ReadValue(ms, schema[branchIndex], enclosingNamespace, namedTypes, depth + 1);
        }

        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var typeElem))
        {
            throw new InvalidDataException("Invalid Avro schema node.");
        }

        if (typeElem.ValueKind != JsonValueKind.String)
        {
            return ReadValue(ms, typeElem, enclosingNamespace, namedTypes, depth + 1);
        }

        var typeName = typeElem.GetString()!;
        var logicalType = schema.TryGetProperty("logicalType", out var lt) && lt.ValueKind == JsonValueKind.String
            ? lt.GetString()
            : null;

        switch (typeName)
        {
            case "record":
            {
                var ns = schema.TryGetProperty("namespace", out var nsProp) && nsProp.ValueKind == JsonValueKind.String
                    ? nsProp.GetString()
                    : enclosingNamespace;
                var record = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (schema.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fields.EnumerateArray())
                    {
                        var fieldName = field.GetProperty("name").GetString()!;
                        var fieldType = field.GetProperty("type");
                        record[fieldName] = ReadValue(ms, fieldType, ns, namedTypes, depth + 1);
                    }
                }

                return record;
            }
            case "enum":
            {
                var symbolIndex = IcebergAvroOcfCodec.ReadInt(ms);
                var symbols = schema.GetProperty("symbols");
                if (symbolIndex < 0 || symbolIndex >= symbols.GetArrayLength())
                {
                    throw new InvalidDataException($"Avro enum index {symbolIndex} out of range.");
                }

                return symbols[symbolIndex].GetString();
            }
            case "fixed":
            {
                var size = schema.GetProperty("size").GetInt32();
                var buf = ReadExactBytes(ms, size);
                return Convert.ToBase64String(buf);
            }
            case "array":
            {
                var itemSchema = schema.GetProperty("items");
                var list = new List<object?>();
                while (true)
                {
                    var count = IcebergAvroOcfCodec.ReadLong(ms);
                    if (count == 0) break;
                    if (count < 0)
                    {
                        count = -count;
                        _ = IcebergAvroOcfCodec.ReadLong(ms);
                    }

                    if (list.Count + count > MaxCollectionItems)
                    {
                        throw new InvalidDataException("Avro array item count exceeded safety limit.");
                    }

                    for (long i = 0; i < count; i++)
                    {
                        list.Add(ReadValue(ms, itemSchema, enclosingNamespace, namedTypes, depth + 1));
                    }
                }

                return list;
            }
            case "map":
            {
                var valueSchema = schema.GetProperty("values");
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                while (true)
                {
                    var count = IcebergAvroOcfCodec.ReadLong(ms);
                    if (count == 0) break;
                    if (count < 0)
                    {
                        count = -count;
                        _ = IcebergAvroOcfCodec.ReadLong(ms);
                    }

                    if (map.Count + count > MaxCollectionItems)
                    {
                        throw new InvalidDataException("Avro map item count exceeded safety limit.");
                    }

                    for (long i = 0; i < count; i++)
                    {
                        var k = IcebergAvroOcfCodec.ReadString(ms);
                        map[k] = ReadValue(ms, valueSchema, enclosingNamespace, namedTypes, depth + 1);
                    }
                }

                return map;
            }
            case "bytes":
            {
                var bytes = IcebergAvroOcfCodec.ReadBytes(ms);
                if (string.Equals(logicalType, "decimal", StringComparison.OrdinalIgnoreCase) &&
                    schema.TryGetProperty("scale", out var scaleElem) &&
                    scaleElem.TryGetInt32(out var scale))
                {
                    return DecodeAvroDecimal(bytes, scale);
                }

                return Convert.ToBase64String(bytes);
            }
            case "long":
            {
                var val = IcebergAvroOcfCodec.ReadLong(ms);
                return val;
            }
            case "int":
            {
                var val = IcebergAvroOcfCodec.ReadInt(ms);
                if (string.Equals(logicalType, "date", StringComparison.OrdinalIgnoreCase))
                {
                    return DateTime.UnixEpoch.AddDays(val).ToString("yyyy-MM-dd");
                }

                return val;
            }
            default:
                return ReadPrimitiveOrNamed(ms, typeName, enclosingNamespace, namedTypes, depth);
        }
    }

    private static object? ReadPrimitiveOrNamed(
        Stream ms,
        string typeName,
        string? enclosingNamespace,
        Dictionary<string, JsonElement> namedTypes,
        int depth)
    {
        switch (typeName)
        {
            case "null":
                return null;
            case "boolean":
                return IcebergAvroOcfCodec.ReadBool(ms);
            case "int":
                return IcebergAvroOcfCodec.ReadInt(ms);
            case "long":
                return IcebergAvroOcfCodec.ReadLong(ms);
            case "float":
                return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(ReadExactBytes(ms, 4)));
            case "double":
                return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(ReadExactBytes(ms, 8)));
            case "string":
                return IcebergAvroOcfCodec.ReadString(ms);
            case "bytes":
                return Convert.ToBase64String(IcebergAvroOcfCodec.ReadBytes(ms));
            default:
            {
                var qualified = !typeName.Contains('.') && !string.IsNullOrWhiteSpace(enclosingNamespace)
                    ? $"{enclosingNamespace}.{typeName}"
                    : typeName;
                if (namedTypes.TryGetValue(qualified, out var resolved) ||
                    namedTypes.TryGetValue(typeName, out resolved))
                {
                    return ReadValue(ms, resolved, enclosingNamespace, namedTypes, depth + 1);
                }

                throw new InvalidDataException($"Unknown Avro type reference '{typeName}'.");
            }
        }
    }

    private static decimal DecodeAvroDecimal(byte[] bigEndianTwosComplement, int scale)
    {
        if (bigEndianTwosComplement.Length == 0) return 0m;
        var unscaled = new System.Numerics.BigInteger(bigEndianTwosComplement, isUnsigned: false, isBigEndian: true);
        var divisor = (decimal)Math.Pow(10, Math.Clamp(scale, 0, 18));
        return (decimal)unscaled / divisor;
    }

    private static byte[] ReadExactBytes(Stream ms, int count)
    {
        var buf = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = ms.Read(buf, offset, count - offset);
            if (read <= 0) throw new EndOfStreamException("Unexpected end of Avro binary payload.");
            offset += read;
        }

        return buf;
    }
}
