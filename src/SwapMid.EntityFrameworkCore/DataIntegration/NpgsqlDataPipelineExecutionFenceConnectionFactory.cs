using System.Threading.Tasks;
using Npgsql;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// 为 ETL PostgreSQL advisory fence 创建独占的主库物理会话。
/// 返回的连接尚未打开，生命周期由 fence 完整持有并释放。
/// </summary>
public interface IDataPipelineExecutionFenceConnectionFactory
{
    Task<NpgsqlConnection> CreateAsync();
}

/// <summary>
/// <see cref="IDataPipelineExecutionFenceConnectionFactory"/> 的 Npgsql 实现。
/// 主库连接串必须经 ABP resolver 获取，不能复用外部数据源的 IConfiguration 工厂。
/// </summary>
public sealed class NpgsqlDataPipelineExecutionFenceConnectionFactory
    : IDataPipelineExecutionFenceConnectionFactory, ITransientDependency
{
    private readonly IConnectionStringResolver _connectionStringResolver;

    public NpgsqlDataPipelineExecutionFenceConnectionFactory(
        IConnectionStringResolver connectionStringResolver)
    {
        _connectionStringResolver = connectionStringResolver;
    }

    public async Task<NpgsqlConnection> CreateAsync()
    {
        var rawConnectionString = await _connectionStringResolver.ResolveAsync(
            ConnectionStrings.DefaultConnectionStringName);
        var builder = new NpgsqlConnectionStringBuilder(rawConnectionString)
        {
            Pooling = false,
            Enlist = false,
            KeepAlive = DataPipelineExecutionFenceSessionSettings.HeartbeatSeconds,
            ApplicationName = DataPipelineExecutionFenceSessionSettings.ApplicationName,
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }
}

internal static class DataPipelineExecutionFenceSessionSettings
{
    internal const int HeartbeatSeconds = 5;
    internal const string ApplicationName = "SwapMid-ETL-Fence";
}
