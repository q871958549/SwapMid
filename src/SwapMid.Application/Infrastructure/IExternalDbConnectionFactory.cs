using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.Infrastructure;

/// <summary>
/// 外部关系型数据库连接统一出口（连接串超时注入 + 并发租约保护）。
/// </summary>
public interface IExternalDbConnectionFactory
{
    Task<ExternalDbConnectionLease> OpenSqlServerConnectionAsync(
        string rawConnectionString,
        string resourceKey,
        int commandTimeoutSeconds = ExternalDbConnectionFactory.DefaultCommandTimeoutSeconds,
        CancellationToken ct = default);

    Task<ExternalDbConnectionLease> OpenPostgreSqlConnectionAsync(
        string rawConnectionString,
        string resourceKey,
        int commandTimeoutSeconds = ExternalDbConnectionFactory.DefaultCommandTimeoutSeconds,
        CancellationToken ct = default);

    Task<ExternalDbConnectionLease> OpenMySqlConnectionAsync(
        string rawConnectionString,
        string resourceKey,
        int commandTimeoutSeconds = ExternalDbConnectionFactory.DefaultCommandTimeoutSeconds,
        CancellationToken ct = default);
}

public sealed class ExternalDbConnectionLease : IAsyncDisposable
{
    public DbConnection Connection { get; }
    private readonly IAsyncDisposable _concurrencyPermit;

    public ExternalDbConnectionLease(DbConnection connection, IAsyncDisposable concurrencyPermit)
    {
        Connection = connection;
        _concurrencyPermit = concurrencyPermit;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Connection.DisposeAsync();
        }
        finally
        {
            await _concurrencyPermit.DisposeAsync();
        }
    }
}
