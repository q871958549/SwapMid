using System.Data;
using System.Threading.Tasks;
using Npgsql;
using NSubstitute;
using Shouldly;
using Volo.Abp.Data;
using Xunit;

namespace SwapMid.DataIntegration;

public sealed class NpgsqlDataPipelineExecutionFenceConnectionFactoryTests
{
    [Fact]
    public async Task 应解析主库并强制专用非池化会话参数()
    {
        const string rawConnectionString =
            "Host=db.internal;Port=5544;Database=han;Username=han_user;" +
            "Password=test-only;SSL Mode=Require;Command Timeout=73;" +
            "Pooling=true;Enlist=true;Keepalive=17;Application Name=caller";
        var resolver = Substitute.For<IConnectionStringResolver>();
        resolver.ResolveAsync(ConnectionStrings.DefaultConnectionStringName)
            .Returns(Task.FromResult(rawConnectionString));
        var factory = new NpgsqlDataPipelineExecutionFenceConnectionFactory(
            resolver);

        await using var connection = await factory.CreateAsync();

        connection.State.ShouldBe(ConnectionState.Closed);
        var builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        builder.Host.ShouldBe("db.internal");
        builder.Port.ShouldBe(5544);
        builder.Database.ShouldBe("han");
        builder.Username.ShouldBe("han_user");
        builder.SslMode.ShouldBe(SslMode.Require);
        builder.CommandTimeout.ShouldBe(73);
        builder.Pooling.ShouldBeFalse();
        builder.Enlist.ShouldBeFalse();
        builder.KeepAlive.ShouldBe(5);
        builder.ApplicationName.ShouldBe("SwapMid-ETL-Fence");
        await resolver.Received(1).ResolveAsync(
            ConnectionStrings.DefaultConnectionStringName);
    }
}
