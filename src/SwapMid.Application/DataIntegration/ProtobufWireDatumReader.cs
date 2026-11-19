using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using static SwapMid.DataIntegration.ProtobufSchemaParser;

namespace SwapMid.DataIntegration;

/// <summary>
/// 零依赖纯 .NET Confluent / Redpanda Schema Registry Protobuf 线协议解码器：
/// 解析 <c>0x00</c> + 4 字节 Schema ID 之后的 ZigZag Varint 消息索引数组与 Protobuf 二进制字段流，输出标准 <see cref="JsonNode"/>。
/// </summary>
internal static class ProtobufWireDatumReader
{
    private const int MaxDepth = 24;

    internal static JsonNode? DecodeConfluentPayload(string protoSchemaText, ReadOnlySpan<byte> payloadAfterSchemaId)
    {
        var schema = Parse(protoSchemaText);
        if (schema.Messages.Count == 0)
        {
            throw new InvalidDataException("Protobuf schema does not declare any message types.");
        }

        var offset = 0;
        var messageIndexes = ReadConfluentMessageIndexes(payloadAfterSchemaId, ref offset);
        var targetMessage = ResolveMessageByIndexes(schema, messageIndexes);
        return DecodeMessage(schema, targetMessage, payloadAfterSchemaId[offset..], depth: 0);
    }

    private static List<int> ReadConfluentMessageIndexes(ReadOnlySpan<byte> buf, ref int offset)
    {
        if (offset >= buf.Length) return [0];
        if (buf[offset] == 0x00)
        {
            offset++;
            return [0];
        }

        var count = (int)ReadZigZagVarInt(buf, ref offset);
        if (count < 0 || count > 16)
        {
            throw new InvalidDataException($"Invalid Confluent Protobuf message index array length: {count}.");
        }

        if (count == 0) return [0];
        var indexes = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            indexes.Add((int)ReadZigZagVarInt(buf, ref offset));
        }

        return indexes;
    }

    private static ProtoMessageDef ResolveMessageByIndexes(ProtoFileSchema schema, IReadOnlyList<int> indexes)
    {
        var currentList = schema.Messages;
        ProtoMessageDef? current = null;
        foreach (var idx in indexes)
        {
            if (idx < 0 || idx >= currentList.Count)
            {
                throw new InvalidDataException($"Confluent Protobuf message index {idx} out of range (count {currentList.Count}).");
            }

            current = currentList[idx];
            currentList = current.NestedMessages;
        }

        return current ?? schema.Messages[0];
    }

    private static JsonObject DecodeMessage(ProtoFileSchema schema, ProtoMessageDef msg, ReadOnlySpan<byte> data, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException($"Protobuf message nesting exceeded max depth {MaxDepth}.");
        }

        var result = new JsonObject();
        var offset = 0;
        while (offset < data.Length)
        {
            var tag = ReadRawVarInt64(data, ref offset);
            var fieldNumber = (int)(tag >> 3);
            var wireType = (int)(tag & 0x07);

            if (!msg.FieldsByNumber.TryGetValue(fieldNumber, out var field))
            {
                SkipUnknownField(data, ref offset, wireType);
                continue;
            }

            if (field.IsMap)
            {
                DecodeMapEntry(schema, msg, field, ReadLengthDelimited(data, ref offset), result, depth);
                continue;
            }

            if (field.IsRepeated && wireType == 2 && IsPackableScalar(schema, msg, field.TypeName))
            {
                var packed = ReadLengthDelimited(data, ref offset);
                var arr = result[field.Name] as JsonArray ?? new JsonArray();
                result[field.Name] = arr;
                var pOff = 0;
                while (pOff < packed.Length)
                {
                    arr.Add(DecodeFieldValue(schema, msg, field.TypeName, DefaultWireTypeForScalar(field.TypeName), packed, ref pOff, depth));
                }

                continue;
            }

            var value = DecodeFieldValue(schema, msg, field.TypeName, wireType, data, ref offset, depth);
            if (field.IsRepeated)
            {
                var arr = result[field.Name] as JsonArray ?? new JsonArray();
                result[field.Name] = arr;
                arr.Add(value);
            }
            else
            {
                result[field.Name] = value;
            }
        }

        return result;
    }

    private static void DecodeMapEntry(
        ProtoFileSchema schema,
        ProtoMessageDef parent,
        ProtoFieldDef mapField,
        ReadOnlySpan<byte> entryBytes,
        JsonObject target,
        int depth)
    {
        string? mapKey = null;
        JsonNode? mapVal = null;
        var offset = 0;
        while (offset < entryBytes.Length)
        {
            var tag = ReadRawVarInt64(entryBytes, ref offset);
            var num = (int)(tag >> 3);
            var wt = (int)(tag & 0x07);
            if (num == 1 && mapField.MapKeyType != null)
            {
                mapKey = DecodeFieldValue(schema, parent, mapField.MapKeyType, wt, entryBytes, ref offset, depth + 1)?.ToString();
            }
            else if (num == 2 && mapField.MapValueType != null)
            {
                mapVal = DecodeFieldValue(schema, parent, mapField.MapValueType, wt, entryBytes, ref offset, depth + 1);
            }
            else
            {
                SkipUnknownField(entryBytes, ref offset, wt);
            }
        }

        if (mapKey != null)
        {
            var mapObj = target[mapField.Name] as JsonObject ?? new JsonObject();
            target[mapField.Name] = mapObj;
            mapObj[mapKey] = mapVal;
        }
    }

    private static JsonNode? DecodeFieldValue(
        ProtoFileSchema schema,
        ProtoMessageDef context,
        string typeName,
        int wireType,
        ReadOnlySpan<byte> data,
        ref int offset,
        int depth)
    {
        switch (typeName)
        {
            case "string": return JsonValue.Create(Encoding.UTF8.GetString(ReadLengthDelimited(data, ref offset)));
            case "bytes": return JsonValue.Create(Convert.ToBase64String(ReadLengthDelimited(data, ref offset)));
            case "bool": return JsonValue.Create(ReadRawVarInt64(data, ref offset) != 0);
            case "int32": return JsonValue.Create(unchecked((int)ReadRawVarInt64(data, ref offset)));
            case "uint32": return JsonValue.Create(unchecked((uint)ReadRawVarInt64(data, ref offset)));
            case "sint32": return JsonValue.Create((int)DecodeZigZag(ReadRawVarInt64(data, ref offset)));
            case "int64": return JsonValue.Create(unchecked((long)ReadRawVarInt64(data, ref offset)));
            case "uint64": return JsonValue.Create(ReadRawVarInt64(data, ref offset));
            case "sint64": return JsonValue.Create(DecodeZigZag(ReadRawVarInt64(data, ref offset)));
            case "fixed32": return JsonValue.Create(ReadFixed32(data, ref offset));
            case "sfixed32": return JsonValue.Create(unchecked((int)ReadFixed32(data, ref offset)));
            case "float": return JsonValue.Create(BitConverter.Int32BitsToSingle(unchecked((int)ReadFixed32(data, ref offset))));
            case "fixed64": return JsonValue.Create(ReadFixed64(data, ref offset));
            case "sfixed64": return JsonValue.Create(unchecked((long)ReadFixed64(data, ref offset)));
            case "double": return JsonValue.Create(BitConverter.Int64BitsToDouble(unchecked((long)ReadFixed64(data, ref offset))));
        }

        var enumDef = ResolveEnum(schema, context, typeName);
        if (enumDef != null)
        {
            var rawEnum = unchecked((int)ReadRawVarInt64(data, ref offset));
            return enumDef.NamesByNumber.TryGetValue(rawEnum, out var label)
                ? JsonValue.Create(label)
                : JsonValue.Create(rawEnum);
        }

        var nestedMsg = ResolveMessage(schema, context, typeName);
        if (nestedMsg != null && wireType == 2)
        {
            return DecodeMessage(schema, nestedMsg, ReadLengthDelimited(data, ref offset), depth + 1);
        }

        SkipUnknownField(data, ref offset, wireType);
        return null;
    }

    private static bool IsPackableScalar(ProtoFileSchema schema, ProtoMessageDef context, string typeName)
        => typeName is "bool" or "int32" or "uint32" or "sint32" or "int64" or "uint64" or "sint64"
            or "fixed32" or "sfixed32" or "float" or "fixed64" or "sfixed64" or "double"
           || ResolveEnum(schema, context, typeName) != null;

    private static int DefaultWireTypeForScalar(string typeName) => typeName switch
    {
        "fixed64" or "sfixed64" or "double" => 1,
        "fixed32" or "sfixed32" or "float" => 5,
        _ => 0,
    };

    private static ulong ReadRawVarInt64(ReadOnlySpan<byte> buf, ref int offset)
    {
        ulong result = 0;
        var shift = 0;
        while (offset < buf.Length && shift < 64)
        {
            var b = buf[offset++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }

        throw new EndOfStreamException("Truncated varint in Protobuf payload.");
    }

    private static long ReadZigZagVarInt(ReadOnlySpan<byte> buf, ref int offset)
        => DecodeZigZag(ReadRawVarInt64(buf, ref offset));

    private static long DecodeZigZag(ulong n) => (long)(n >> 1) ^ -((long)(n & 1));

    private static uint ReadFixed32(ReadOnlySpan<byte> buf, ref int offset)
    {
        if (offset + 4 > buf.Length) throw new EndOfStreamException("Truncated fixed32 in Protobuf payload.");
        var val = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(offset, 4));
        offset += 4;
        return val;
    }

    private static ulong ReadFixed64(ReadOnlySpan<byte> buf, ref int offset)
    {
        if (offset + 8 > buf.Length) throw new EndOfStreamException("Truncated fixed64 in Protobuf payload.");
        var val = BinaryPrimitives.ReadUInt64LittleEndian(buf.Slice(offset, 8));
        offset += 8;
        return val;
    }

    private static ReadOnlySpan<byte> ReadLengthDelimited(ReadOnlySpan<byte> buf, ref int offset)
    {
        var len = checked((int)ReadRawVarInt64(buf, ref offset));
        if (len < 0 || offset + len > buf.Length) throw new EndOfStreamException("Truncated length-delimited Protobuf field.");
        var slice = buf.Slice(offset, len);
        offset += len;
        return slice;
    }

    private static void SkipUnknownField(ReadOnlySpan<byte> buf, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case 0: _ = ReadRawVarInt64(buf, ref offset); break;
            case 1: _ = ReadFixed64(buf, ref offset); break;
            case 2: _ = ReadLengthDelimited(buf, ref offset); break;
            case 5: _ = ReadFixed32(buf, ref offset); break;
            default: throw new InvalidDataException($"Unsupported Protobuf wire type {wireType}.");
        }
    }
}
