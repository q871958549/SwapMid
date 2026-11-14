using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace SwapMid.DataIntegration;

internal sealed class ScriptedDataReader
    : DbDataReader, IDbColumnSchemaGenerator
{
    private static readonly ReadOnlyCollection<DbColumn> Columns =
        Array.AsReadOnly<DbColumn>(
        [
            new RecordingDbColumn("id", 0, typeof(int), "int"),
            new RecordingDbColumn("value", 1, typeof(int), "int"),
        ]);
    private static readonly ReadOnlyCollection<DbColumn> NullColumn =
        Array.AsReadOnly<DbColumn>(
        [
            new RecordingDbColumn("optional_value", 0, typeof(object), "NULL"),
        ]);

    private readonly int _trueRows;
    private readonly int _maxReadCalls;
    private readonly CancellationToken _requestToken;
    private readonly bool _providerNullColumn;
    private readonly Action<string>? _recordEvent;
    private int _currentRow;

    public ScriptedDataReader(
        int trueRows,
        int maxReadCalls,
        CancellationToken requestToken,
        bool providerNullColumn = false,
        Action<string>? recordEvent = null)
    {
        _trueRows = trueRows;
        _maxReadCalls = maxReadCalls;
        _requestToken = requestToken;
        _providerNullColumn = providerNullColumn;
        _recordEvent = recordEvent;
    }

    public int ReadCalls { get; private set; }
    public int ValueReadRows { get; private set; }
    public bool AllReadTokensMatched { get; private set; } = true;
    public CancellationToken? ExecutionToken { get; private set; }
    public bool IsDisposed { get; private set; }

    public override int FieldCount => _providerNullColumn ? 1 : 2;
    public override bool HasRows => _trueRows > 0;
    public override bool IsClosed => IsDisposed;
    public override int RecordsAffected => -1;
    public override int Depth => 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public ReadOnlyCollection<DbColumn> GetColumnSchema()
        => _providerNullColumn ? NullColumn : Columns;

    public override bool Read()
        => ReadCore(CancellationToken.None);

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(ReadCore(cancellationToken));

    public override bool NextResult()
        => false;

    public override Task<bool> NextResultAsync(
        CancellationToken cancellationToken)
        => Task.FromResult(false);

    public override string GetName(int ordinal)
        => ordinal switch
        {
            0 => "id",
            1 => "value",
            _ => throw new IndexOutOfRangeException(),
        };

    public override int GetOrdinal(string name)
        => name switch
        {
            "id" => 0,
            "value" => 1,
            _ => throw new IndexOutOfRangeException(name),
        };

    public override Type GetFieldType(int ordinal)
        => _providerNullColumn ? typeof(object) : typeof(int);

    public override string GetDataTypeName(int ordinal)
        => _providerNullColumn ? "NULL" : "int";

    public override object GetValue(int ordinal)
    {
        ValueReadRows = Math.Max(ValueReadRows, _currentRow);
        return ordinal switch
        {
            0 => _currentRow,
            1 => checked(_currentRow * 10),
            _ => throw new IndexOutOfRangeException(),
        };
    }

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            values[ordinal] = GetValue(ordinal);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal)
        => _providerNullColumn;

    public override Task<bool> IsDBNullAsync(
        int ordinal,
        CancellationToken cancellationToken)
        => Task.FromResult(_providerNullColumn);

    public override bool GetBoolean(int ordinal)
        => Convert.ToBoolean(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override byte GetByte(int ordinal)
        => Convert.ToByte(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override long GetBytes(
        int ordinal,
        long dataOffset,
        byte[]? buffer,
        int bufferOffset,
        int length)
        => throw new NotSupportedException();

    public override char GetChar(int ordinal)
        => Convert.ToChar(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override long GetChars(
        int ordinal,
        long dataOffset,
        char[]? buffer,
        int bufferOffset,
        int length)
        => throw new NotSupportedException();

    public override Guid GetGuid(int ordinal)
        => (Guid)GetValue(ordinal);

    public override short GetInt16(int ordinal)
        => Convert.ToInt16(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override int GetInt32(int ordinal)
        => Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override long GetInt64(int ordinal)
        => Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override float GetFloat(int ordinal)
        => Convert.ToSingle(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override double GetDouble(int ordinal)
        => Convert.ToDouble(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override string GetString(int ordinal)
        => Convert.ToString(GetValue(ordinal), CultureInfo.InvariantCulture)!;

    public override decimal GetDecimal(int ordinal)
        => Convert.ToDecimal(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override DateTime GetDateTime(int ordinal)
        => Convert.ToDateTime(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override IEnumerator GetEnumerator()
        => Array.Empty<object>().GetEnumerator();

    public override ValueTask DisposeAsync()
    {
        MarkDisposed();
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        MarkDisposed();
        base.Dispose(disposing);
    }

    private void MarkDisposed()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        _recordEvent?.Invoke("reader.dispose");
    }

    private bool ReadCore(CancellationToken token)
    {
        ReadCalls++;
        if (ReadCalls > _maxReadCalls)
        {
            throw new XunitException(
                $"runner 发起了禁止的第 {ReadCalls} 次 ReadAsync。");
        }

        ExecutionToken ??= token;
        if (token != ExecutionToken.Value ||
            token == _requestToken ||
            !token.CanBeCanceled)
        {
            AllReadTokensMatched = false;
            throw new XunitException(
                "ReadAsync 未持续使用独立且可取消的整次执行 token。");
        }

        if (ReadCalls > _trueRows)
        {
            return false;
        }

        _currentRow = ReadCalls;
        return true;
    }
}

internal sealed class RecordingDbColumn : DbColumn
{
    public RecordingDbColumn(
        string name,
        int ordinal,
        Type type,
        string typeName)
    {
        ColumnName = name;
        ColumnOrdinal = ordinal;
        DataType = type;
        DataTypeName = typeName;
        AllowDBNull = false;
    }
}
