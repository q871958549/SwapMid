using System.Text.RegularExpressions;

namespace SwapMid.Infrastructure;

/// <summary>
/// 连接串级默认命令超时注入（统一外部数据访问网关 Dapper 侧核心机制，见 ADR-0010 与
/// docs/external-data-gateway-blueprint.md §4.2）：三家 provider（SqlClient/MySqlConnector/Npgsql）
/// 均支持连接串关键字设默认命令超时，注入一次即让全部经该连接的 Dapper 调用受控，零手改调用点。
/// ⚠️ 纯字符串级追加，禁用 XxxConnectionStringBuilder 解析——本仓有 PG 串被误传进
/// SqlServerConnectionStringHelper 的路径（<c>ExternalDbSchemaReader.cs</c>），builder 全量解析会
/// 因未知键直接抛异常炸掉该路径；字符串追加对 SqlServer/PG（同用 "Command Timeout"）与
/// MySQL（"Default Command Timeout"）均合法。
/// </summary>
internal static class ConnectionStringTimeoutInjector
{
    // 负向后顾：避免 "Default Command Timeout" 被误判为已含 "Command Timeout"。
    private static readonly Regex SqlServerOrPostgresKeyword = new(
        @"(?<!default\s)command\s*timeout\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MySqlKeyword = new(
        @"default\s*command\s*timeout\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>SqlServer / PostgreSQL 连接串追加 "Command Timeout"（两家共用同一关键字）。</summary>
    public static string AppendSqlServerOrPostgresCommandTimeout(string connectionString, int seconds)
        => Append(connectionString, SqlServerOrPostgresKeyword, "Command Timeout", seconds);

    /// <summary>MySQL 连接串追加 "Default Command Timeout"。</summary>
    public static string AppendMySqlCommandTimeout(string connectionString, int seconds)
        => Append(connectionString, MySqlKeyword, "Default Command Timeout", seconds);

    private static string Append(string connectionString, Regex keywordPattern, string keyword, int seconds)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || keywordPattern.IsMatch(connectionString))
        {
            // 已含关键字（显式配置）→ 尊重现状，原样返回；显式 Dapper commandTimeout 参数优先级更高，两者互不冲突。
            return connectionString;
        }

        return connectionString.TrimEnd(';') + $";{keyword}={seconds};";
    }
}
