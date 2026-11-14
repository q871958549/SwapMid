using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace SwapMid.DataIntegration;

internal sealed class RecordingDbConnection : DbConnection
{
    private readonly ScriptedDataReader? _sourceReader;
    private readonly RecordingDbTransaction? _targetTransaction;
    private readonly Func<
        RecordingDbCommand,
        CancellationToken,
        RecordingCommandResult>? _executeTarget;
    private ConnectionState _state = ConnectionState.Closed;
    private string _connectionString = string.Empty;

    private RecordingDbConnection(
        ScriptedDataReader? sourceReader,
        RecordingDbTransaction? targetTransaction,
        Func<
            RecordingDbCommand,
            CancellationToken,
            RecordingCommandResult>? executeTarget)
    {
        _sourceReader = sourceReader;
        _targetTransaction = targetTransaction;
        _executeTarget = executeTarget;
        if (_targetTransaction != null)
        {
            _targetTransaction.Attach(this);
        }

        if (_sourceReader != null)
        {
            SourceCommand = CreateRecordingCommand();
        }
    }

    public static RecordingDbConnection ForSource(
        ScriptedDataReader reader)
        => new(reader, null, null);

    public static RecordingDbConnection ForTarget(
        RecordingDbTransaction transaction,
        Func<
            RecordingDbCommand,
            CancellationToken,
            RecordingCommandResult> executeTarget)
        => new(null, transaction, executeTarget);

    public RecordingDbCommand? SourceCommand { get; }
    public List<RecordingDbCommand> CreatedCommands { get; } = [];
    public List<CancellationToken> OpenTokens { get; } = [];
    public List<CancellationToken> BeginTransactionTokens { get; } = [];
    public bool IsDisposed { get; private set; }
    public int DisposeAsyncCalls { get; private set; }
    public Exception? DisposeAsyncException { get; set; }

    [AllowNull]
    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value ?? string.Empty;
    }

    public override string Database => "recording";
    public override string DataSource => "recording";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Close()
    {
        _state = ConnectionState.Closed;
    }

    public override void Open()
    {
        _state = ConnectionState.Open;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        OpenTokens.Add(cancellationToken);
        _state = ConnectionState.Open;
        return Task.CompletedTask;
    }

    protected override DbTransaction BeginDbTransaction(
        IsolationLevel isolationLevel)
    {
        if (_targetTransaction == null)
        {
            throw new XunitException("源连接不应创建事务。");
        }

        _targetTransaction.RecordBegin(isolationLevel);
        return _targetTransaction;
    }

    protected override ValueTask<DbTransaction> BeginDbTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        if (_targetTransaction == null)
        {
            throw new XunitException("源连接不应创建事务。");
        }

        BeginTransactionTokens.Add(cancellationToken);
        _targetTransaction.RecordBegin(isolationLevel);
        return ValueTask.FromResult<DbTransaction>(_targetTransaction);
    }

    protected override DbCommand CreateDbCommand()
    {
        if (_sourceReader != null && SourceCommand != null)
        {
            return SourceCommand;
        }

        return CreateRecordingCommand();
    }

    public override ValueTask DisposeAsync()
    {
        DisposeAsyncCalls++;
        IsDisposed = true;
        _state = ConnectionState.Closed;
        return DisposeAsyncException is { } exception
            ? new ValueTask(Task.FromException(exception))
            : ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        _state = ConnectionState.Closed;
        base.Dispose(disposing);
    }

    private RecordingDbCommand CreateRecordingCommand()
    {
        var command = new RecordingDbCommand(
            this,
            _sourceReader,
            _executeTarget);
        CreatedCommands.Add(command);
        return command;
    }
}

internal sealed class RecordingDbTransaction : DbTransaction
{
    private readonly Action<string>? _recordEvent;
    private DbConnection? _connection;

    public RecordingDbTransaction(Action<string>? recordEvent = null)
    {
        _recordEvent = recordEvent;
    }

    public int BeginCalls { get; private set; }
    public int CommitCalls { get; private set; }
    public int RollbackCalls { get; private set; }
    public IsolationLevel BegunIsolationLevel { get; private set; }
    public List<CancellationToken> CommitTokens { get; } = [];
    public List<CancellationToken> RollbackTokens { get; } = [];
    public bool IsDisposed { get; private set; }
    public Exception? CommitException { get; set; }
    public Exception? RollbackException { get; set; }
    public Exception? DisposeAsyncException { get; set; }

    public override IsolationLevel IsolationLevel => BegunIsolationLevel;
    protected override DbConnection? DbConnection => _connection;

    public void Attach(DbConnection connection)
    {
        _connection = connection;
    }

    public void RecordBegin(IsolationLevel isolationLevel)
    {
        BeginCalls++;
        BegunIsolationLevel = isolationLevel;
        _recordEvent?.Invoke("target.begin");
    }

    public override void Commit()
    {
        CommitCalls++;
        CommitTokens.Add(CancellationToken.None);
        if (CommitException is { } exception)
        {
            throw exception;
        }
    }

    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        CommitCalls++;
        CommitTokens.Add(cancellationToken);
        return CommitException is { } exception
            ? Task.FromException(exception)
            : Task.CompletedTask;
    }

    public override void Rollback()
    {
        RollbackCalls++;
        RollbackTokens.Add(CancellationToken.None);
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        RollbackCalls++;
        RollbackTokens.Add(cancellationToken);
        return RollbackException is { } exception
            ? Task.FromException(exception)
            : Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return DisposeAsyncException is { } exception
            ? new ValueTask(Task.FromException(exception))
            : ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class RecordingDbCommand : DbCommand
{
    private readonly ScriptedDataReader? _reader;
    private readonly Func<
        RecordingDbCommand,
        CancellationToken,
        RecordingCommandResult>? _executeTarget;
    private readonly RecordingDbParameterCollection _parameters = new();
    private DbConnection? _connection;
    private DbTransaction? _transaction;
    private string _commandText = string.Empty;

    public RecordingDbCommand(
        DbConnection connection,
        ScriptedDataReader? reader,
        Func<
            RecordingDbCommand,
            CancellationToken,
            RecordingCommandResult>? executeTarget)
    {
        _connection = connection;
        _reader = reader;
        _executeTarget = executeTarget;
    }

    public List<CancellationToken> ReaderTokens { get; } = [];
    public List<CommandBehavior> ReaderBehaviors { get; } = [];
    public bool IsDisposed { get; private set; }

    [AllowNull]
    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }

    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = value;
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction
    {
        get => _transaction;
        set => _transaction = value;
    }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery()
        => _executeTarget?.Invoke(this, CancellationToken.None).AffectedRows
           ?? throw new XunitException("源命令不应执行 ExecuteNonQuery。");

    public override Task<int> ExecuteNonQueryAsync(
        CancellationToken cancellationToken)
        => Task.FromResult(
            _executeTarget?.Invoke(this, cancellationToken).AffectedRows
            ?? throw new XunitException("源命令不应执行 ExecuteNonQueryAsync。"));

    public override object? ExecuteScalar()
        => throw new XunitException("测试未声明 ExecuteScalar。");

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter()
        => new RecordingDbParameter();

    protected override DbDataReader ExecuteDbDataReader(
        CommandBehavior behavior)
        => ExecuteReaderCore(behavior, CancellationToken.None);

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
        => Task.FromResult<DbDataReader>(
            ExecuteReaderCore(behavior, cancellationToken));

    public override ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }

    private DbDataReader ExecuteReaderCore(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        ReaderBehaviors.Add(behavior);
        ReaderTokens.Add(cancellationToken);
        if (_reader != null)
        {
            return _reader;
        }

        if (_executeTarget != null)
        {
            var resultValue = _executeTarget(this, cancellationToken);
            if (resultValue.Reader != null)
            {
                return resultValue.Reader;
            }

            var result = new DataTable();
            result.Columns.Add("value", typeof(long));
            result.Rows.Add((long)resultValue.AffectedRows);
            return result.CreateDataReader();
        }

        throw new XunitException("命令没有已声明的 reader 结果。");
    }
}

internal sealed class RecordingDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = [];

    public override int Count => _items.Count;
    public override object SyncRoot => ((ICollection)_items).SyncRoot;

    public override int Add(object value)
    {
        _items.Add((DbParameter)value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
        {
            Add(value!);
        }
    }

    public override void Clear()
    {
        _items.Clear();
    }

    public override bool Contains(object value)
        => _items.Contains((DbParameter)value);

    public override bool Contains(string value)
        => IndexOf(value) >= 0;

    public override void CopyTo(Array array, int index)
        => ((ICollection)_items).CopyTo(array, index);

    public override IEnumerator GetEnumerator()
        => _items.GetEnumerator();

    public override int IndexOf(object value)
        => _items.IndexOf((DbParameter)value);

    public override int IndexOf(string parameterName)
        => _items.FindIndex(parameter =>
            string.Equals(
                parameter.ParameterName,
                parameterName,
                StringComparison.OrdinalIgnoreCase));

    public override void Insert(int index, object value)
        => _items.Insert(index, (DbParameter)value);

    public override void Remove(object value)
        => _items.Remove((DbParameter)value);

    public override void RemoveAt(int index)
        => _items.RemoveAt(index);

    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            RemoveAt(index);
        }
    }

    protected override DbParameter GetParameter(int index)
        => _items[index];

    protected override DbParameter GetParameter(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
        {
            throw new IndexOutOfRangeException(parameterName);
        }

        return _items[index];
    }

    protected override void SetParameter(int index, DbParameter value)
        => _items[index] = value;

    protected override void SetParameter(
        string parameterName,
        DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
        {
            _items.Add(value);
        }
        else
        {
            _items[index] = value;
        }
    }
}

internal sealed class RecordingDbParameter : DbParameter
{
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
        = ParameterDirection.Input;
    public override bool IsNullable { get; set; }
    [AllowNull]
    public override string ParameterName { get; set; } = string.Empty;
    [AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;
    public override object? Value { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }

    public override void ResetDbType()
    {
    }
}
