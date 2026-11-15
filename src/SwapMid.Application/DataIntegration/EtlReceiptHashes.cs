using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SwapMid.DataIntegration;

/// <summary>receipt-v1 的版本化、长度前缀摘要；禁止用字符串拼接替代。</summary>
internal static class EtlReceiptHashes
{
    internal static string ComputeTargetResource(
        string provider,
        Guid resourceInstanceId,
        string canonicalTargetTable)
    {
        using var hash = new TlvHash("swapmid-etl-target-resource-v2");
        hash.Add(provider);
        hash.Add(resourceInstanceId);
        hash.Add(canonicalTargetTable);
        return hash.CompleteHex();
    }

    internal static string ComputeCoordinationIdentity(
        string provider,
        params string[] identityParts)
    {
        using var hash = new TlvHash("swapmid-etl-target-coordination-v1");
        hash.Add(provider);
        hash.Add(identityParts.Length);
        foreach (var part in identityParts)
        {
            hash.Add(part);
        }

        return hash.CompleteHex();
    }

    internal static byte EncodeWriteMode(EtlWriteMode writeMode) =>
        writeMode switch
        {
            EtlWriteMode.Append => 1,
            EtlWriteMode.Truncate => 2,
            EtlWriteMode.Upsert => 3,
            _ => throw EtlPipelineRunner.InvalidInput(),
        };

    internal static string ComputeExecutionContract(
        Guid pipelineId,
        Guid sourceDataSourceId,
        string sourceConfigVersion,
        Guid targetDataSourceId,
        string targetConfigVersion,
        string provider,
        Guid resourceInstanceId,
        string targetCoordinationIdentityHash,
        string legacyTargetResourceHash,
        string verifiedTargetResourceHash,
        string canonicalTargetTable,
        EtlWriteMode writeMode,
        string sourceQueryHash,
        IReadOnlyList<string>? upsertKeyColumns = null,
        string? upsertWatermarkColumn = null)
    {
        using var hash = new TlvHash("swapmid-etl-execution-contract-v1");
        hash.Add(pipelineId);
        hash.Add(sourceDataSourceId);
        hash.Add(sourceConfigVersion);
        hash.Add(targetDataSourceId);
        hash.Add(targetConfigVersion);
        hash.Add(provider);
        hash.Add(resourceInstanceId);
        hash.AddHex(targetCoordinationIdentityHash);
        hash.AddHex(legacyTargetResourceHash);
        hash.AddHex(verifiedTargetResourceHash);
        hash.Add(canonicalTargetTable);
        hash.Add(EncodeWriteMode(writeMode));
        hash.AddHex(sourceQueryHash);
        if (writeMode == EtlWriteMode.Upsert)
        {
            if (upsertKeyColumns == null || upsertKeyColumns.Count == 0)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            hash.Add(upsertKeyColumns.Count);
            foreach (var key in upsertKeyColumns)
            {
                hash.Add(key.ToUpperInvariant());
            }

            if (string.IsNullOrWhiteSpace(upsertWatermarkColumn))
            {
                hash.AddNull();
            }
            else
            {
                hash.Add(upsertWatermarkColumn.ToUpperInvariant());
            }
        }

        return hash.CompleteHex();
    }

    internal static string ComputeSchemaHash(string schemaFingerprint)
    {
        using var hash = new TlvHash("swapmid-etl-target-schema-v1");
        hash.Add(schemaFingerprint);
        return hash.CompleteHex();
    }

    internal sealed class BatchDigest : IDisposable
    {
        private readonly TlvHash _hash = new("swapmid-etl-batch-digest-v1");
        private long _rows;
        private bool _completed;

        internal BatchDigest(IReadOnlyList<string> columns)
        {
            _hash.Add(columns.Count);
            foreach (var column in columns)
            {
                _hash.Add(column);
            }
        }

        internal void AddRow(IReadOnlyList<object?> values)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _rows++;
            _hash.Add(_rows);
            _hash.Add(values.Count);
            foreach (var value in values)
            {
                AddValue(value);
            }
        }

        internal string Complete(long totalRows)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            if (_rows != totalRows)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            _hash.Add(totalRows);
            _completed = true;
            return _hash.CompleteHex();
        }

        public void Dispose()
        {
            _completed = true;
            _hash.Dispose();
        }

        private void AddValue(object? value)
        {
            switch (value)
            {
                case null:
                    _hash.AddNull();
                    break;
                case bool item:
                    _hash.Add((byte)0x01, item ? (byte)1 : (byte)0);
                    break;
                case byte item:
                    _hash.Add((byte)0x02, item);
                    break;
                case sbyte item:
                    _hash.Add((byte)0x03, unchecked((byte)item));
                    break;
                case short item:
                    _hash.Add((byte)0x04, item);
                    break;
                case ushort item:
                    _hash.Add((byte)0x05, item);
                    break;
                case int item:
                    _hash.Add((byte)0x06, item);
                    break;
                case uint item:
                    _hash.Add((byte)0x07, item);
                    break;
                case long item:
                    _hash.Add((byte)0x08, item);
                    break;
                case ulong item:
                    _hash.Add((byte)0x09, item);
                    break;
                case float item:
                    _hash.Add((byte)0x0A, BitConverter.SingleToInt32Bits(item));
                    break;
                case double item:
                    _hash.Add((byte)0x0B, BitConverter.DoubleToInt64Bits(item));
                    break;
                case decimal item:
                    _hash.Add((byte)0x0C);
                    foreach (var part in decimal.GetBits(item))
                    {
                        _hash.Add(part);
                    }
                    break;
                case string item:
                    _hash.Add((byte)0x0D, item);
                    break;
                case byte[] item:
                    _hash.Add((byte)0x0E, item);
                    break;
                case Guid item:
                    _hash.Add((byte)0x0F, item);
                    break;
                case DateTime item:
                    _hash.Add((byte)0x10);
                    _hash.Add(item.Ticks);
                    break;
                case DateTimeOffset item:
                    _hash.Add((byte)0x11);
                    _hash.Add(item.Ticks);
                    _hash.Add(checked((short)item.Offset.TotalMinutes));
                    break;
                case TimeSpan item:
                    _hash.Add((byte)0x12, item.Ticks);
                    break;
                case DateOnly item:
                    _hash.Add((byte)0x13, item.DayNumber);
                    break;
                case TimeOnly item:
                    _hash.Add((byte)0x14, item.Ticks);
                    break;
                default:
                    throw EtlPipelineRunner.InvalidInput();
            }
        }
    }

    private sealed class TlvHash : IDisposable
    {
        private readonly IncrementalHash _hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _completed;

        internal TlvHash(string domain)
        {
            Add(domain);
        }

        internal void AddNull() => Append([0x00]);

        internal void Add(byte value) => Add((byte)0x01, value);
        internal void Add(short value) => Add((byte)0x02, value);
        internal void Add(ushort value) => Add((byte)0x03, value);
        internal void Add(int value) => Add((byte)0x04, value);
        internal void Add(uint value) => Add((byte)0x05, value);
        internal void Add(long value) => Add((byte)0x06, value);
        internal void Add(ulong value) => Add((byte)0x07, value);
        internal void Add(Guid value) => Add((byte)0x08, value);
        internal void Add(string value) => Add((byte)0x09, value);

        internal void Add(byte tag, byte value) => Add(tag, [value]);

        internal void Add(byte tag, short value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(short)];
            BinaryPrimitives.WriteInt16BigEndian(bytes, value);
            Add(tag, bytes);
        }

        internal void Add(byte tag, ushort value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
            Add(tag, bytes);
        }

        internal void Add(byte tag, int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            Add(tag, bytes);
        }

        internal void Add(byte tag, uint value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            Add(tag, bytes);
        }

        internal void Add(byte tag, long value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            Add(tag, bytes);
        }

        internal void Add(byte tag, ulong value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            Add(tag, bytes);
        }

        internal void Add(byte tag, Guid value) =>
            Add(tag, value.ToByteArray(bigEndian: true));

        internal void Add(byte tag, string value) =>
            Add(tag, Encoding.UTF8.GetBytes(value));

        internal void Add(byte tag, ReadOnlySpan<byte> value)
        {
            Span<byte> header = stackalloc byte[sizeof(int) + 1];
            header[0] = tag;
            BinaryPrimitives.WriteInt32BigEndian(header[1..], value.Length);
            Append(header);
            Append(value);
        }

        internal void AddHex(string value)
        {
            if (value.Length != PipelineExecutionConsts.Sha256HexLength)
            {
                throw EtlPipelineRunner.InvalidInput();
            }

            try
            {
                Add((byte)0x0A, Convert.FromHexString(value));
            }
            catch (FormatException)
            {
                throw EtlPipelineRunner.InvalidInput();
            }
        }

        internal string CompleteHex()
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _completed = true;
            return Convert.ToHexString(_hash.GetHashAndReset());
        }

        public void Dispose()
        {
            _completed = true;
            _hash.Dispose();
        }

        private void Append(ReadOnlySpan<byte> value) => _hash.AppendData(value);
    }
}
