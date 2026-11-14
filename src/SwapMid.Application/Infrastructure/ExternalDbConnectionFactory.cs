using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Npgsql;
using Volo.Abp.DependencyInjection;

namespace SwapMid.Infrastructure;

public class ExternalDbConnectionFactory : IExternalDbConnectionFactory, ISingletonDependency
{
    public const int DefaultCommandTimeoutSeconds = 30;
    public const int DefaultMaxConcurrency = 8;

    private readonly IExternalDataSourceConcurrencyGate _gate;
    private readonly IConfiguration _configuration;

    public ExternalDbConnectionFactory(IExternalDataSourceConcurrencyGate gate, IConfiguration configuration)
    {
        _gate = gate;
        _configuration = configuration;
    }

    public async Task<ExternalDbConnectionLease> OpenSqlServerConnectionAsync(
        string rawConnectionString,
        string resourceKey,
        int commandTimeoutSeconds = DefaultCommandTimeoutSeconds,
        CancellationToken ct = default)
    {
        var normalized = SqlServerConnectionStringHelper.Normalize(rawConnectionString);
        var withTimeout = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(
            normalized, ResolveTimeout(resourceKey, commandTimeoutSeconds));

        var permit = await AcquirePermitAsync(resourceKey, ct);
        try
        {
            var connection = new SqlConnection(withTimeout);
            await connection.OpenAsync(ct);
            return new ExternalDbConnectionLease(connection, permit);
        }
        catch
        {
            await permit.DisposeAsync();
            throw;
        }
    }

    public async Task<ExternalDbConnectionLease> OpenPostgreSqlConnectionAsync(
        string rawConnectionString,
        string resourceKey,
        int commandTimeoutSeconds = DefaultCommandTimeoutSeconds,
        CancellationToken ct = default)
    {
        var withTimeout = ConnectionStringTimeoutInjector.AppendSqlServerOrPostgresCommandTimeout(
            rawConnectionString, ResolveTimeout(resourceKey, commandTimeoutSeconds));

        var permit = await AcquirePermitAsync(resourceKey, ct);
        try
        {
            var connection = new NpgsqlConnection(withTimeout);
            await connection.OpenAsync(ct);
            return new ExternalDbConnectionLease(connection, permit);
        }
        catch
        {
            await permit.DisposeAsync();
            throw;
        }
    }

    public async Task<ExternalDbConnectionLease> OpenMySqlConnectionAsync(
        string rawConnectionString,
        string resourceKey,
        int commandTimeoutSeconds = DefaultCommandTimeoutSeconds,
        CancellationToken ct = default)
    {
        var withTimeout = ConnectionStringTimeoutInjector.AppendMySqlCommandTimeout(
            rawConnectionString, ResolveTimeout(resourceKey, commandTimeoutSeconds));

        var permit = await AcquirePermitAsync(resourceKey, ct);
        try
        {
            var connection = new MySqlConnection(withTimeout);
            await connection.OpenAsync(ct);
            return new ExternalDbConnectionLease(connection, permit);
        }
        catch
        {
            await permit.DisposeAsync();
            throw;
        }
    }

    private async Task<IAsyncDisposable> AcquirePermitAsync(string resourceKey, CancellationToken ct)
    {
        var maxConcurrency = _configuration.GetValue<int?>($"ExternalDb:{resourceKey}:MaxConcurrency")
            ?? DefaultMaxConcurrency;
        var permit = await _gate.TryAcquireAsync($"extdb:{resourceKey}", maxConcurrency, ct);
        return permit ?? throw new InvalidOperationException($"外部数据库并发已达上限：{resourceKey}");
    }

    private int ResolveTimeout(string resourceKey, int callerDefault)
        => _configuration.GetValue<int?>($"ExternalDb:{resourceKey}:CommandTimeoutSeconds")
           ?? callerDefault;
}
