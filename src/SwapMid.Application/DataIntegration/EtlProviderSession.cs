using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;
using Npgsql;

namespace SwapMid.DataIntegration;

/// <summary>
/// 在任何源查询、目标目录读取或事务之前固定 provider 会话语义，避免相同 CLR 值在
/// 两台数据库的登录默认设置下代表不同的持久值。
/// </summary>
internal static class EtlProviderSession
{
    internal static Task ConfigureAsync(
        DbConnection connection,
        RelationalDataSourceConnection.Probe probe,
        int commandTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is NpgsqlConnection &&
            probe.NormalizedProvider is "postgresql" or "postgres" or "npgsql")
        {
            return connection.ExecuteAsync(new CommandDefinition(
                "SET TIME ZONE 'UTC'; SET standard_conforming_strings = on;",
                commandTimeout: commandTimeoutSeconds,
                cancellationToken: ct));
        }

        if (connection is not MySqlConnection ||
            probe.NormalizedProvider is not ("mysql" or "mariadb"))
        {
            return Task.CompletedTask;
        }

        // TIMESTAMP 在读取和写入时都会按 session time_zone 转换。源与目标必须先统一
        // 到 UTC；否则跨服务器默认时区不同会把相同 wall-clock 写成不同 instant。
        // MySqlConnector 的 NoBackslashEscapes=false 也要求会话保留反斜杠转义语义；
        // 池化连接每次打开后都重新固定，不能继承服务器或前一租户的 sql_mode。
        return connection.ExecuteAsync(new CommandDefinition(
            """
            SET
                @@SESSION.time_zone = '+00:00',
                @@SESSION.sql_mode = TRIM(
                    BOTH ',' FROM REPLACE(
                        CONCAT(',', @@SESSION.sql_mode, ','),
                        ',NO_BACKSLASH_ESCAPES,',
                        ','))
            """,
            commandTimeout: commandTimeoutSeconds,
            cancellationToken: ct));
    }
}
