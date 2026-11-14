using System;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 专用关系库连接创建边界。生产实现仍统一复用
/// <see cref="RelationalDataSourceConnection"/>；测试可替换为独立的进程内数据库，
/// 从而验证真实 reader / transaction 行为而不连接生产数据源。
/// </summary>
public interface IEtlRelationalConnectionFactory
{
    DbConnection Create(string configJson, int connectTimeoutSeconds);

    DbConnection CreateTarget(
        string configJson,
        int connectTimeoutSeconds,
        int commandTimeoutSeconds)
        => Create(configJson, connectTimeoutSeconds);
}

/// <summary><see cref="IEtlRelationalConnectionFactory"/> 的生产实现。</summary>
public sealed class EtlRelationalConnectionFactory
    : IEtlRelationalConnectionFactory, ITransientDependency
{
    public DbConnection Create(string configJson, int connectTimeoutSeconds)
        => RelationalDataSourceConnection.Create(
            RelationalDataSourceConnection.Parse(configJson),
            connectTimeoutSeconds);

    public DbConnection CreateTarget(
        string configJson,
        int connectTimeoutSeconds,
        int commandTimeoutSeconds)
        => RelationalDataSourceConnection.Create(
            RelationalDataSourceConnection.Parse(configJson),
            connectTimeoutSeconds,
            isolateMySqlPhysicalSession: true,
            mysqlHardCommandTimeoutSeconds: commandTimeoutSeconds);
}

/// <summary>
/// 关系型数据源连接:解析 <see cref="DataSource.Config"/> JSON → 建未打开的 <see cref="DbConnection"/>(SqlServer/MySQL)。
/// 由 ETL(<see cref="EtlPipelineRunner"/>)与 <see cref="DataSourceAppService"/> 连接测试共用，
/// 避免 provider/host/port/database/username/password/TLS/目标资源组解析发生漂移。
/// </summary>
internal static class RelationalDataSourceConnection
{
    public sealed class Probe
    {
        public string? Provider { get; init; }
        public string? Host { get; init; }
        public int Port { get; init; }
        public string? Database { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
        public string? TargetResourceGroup { get; init; }
        public bool Encrypt { get; init; }
        public bool TrustServerCertificate { get; init; }

        public string NormalizedProvider =>
            string.IsNullOrWhiteSpace(Provider) ? "sqlserver" : Provider.Trim().ToLowerInvariant();
    }

    public static Probe Parse(string configJson)
    {
        using var doc = JsonDocument.Parse(configJson);
        var root = doc.RootElement;

        string? Str(params string[] names)
        {
            foreach (var name in names)
                if (root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String)
                    return e.GetString();
            return null;
        }

        int Int(string name)
        {
            if (root.TryGetProperty(name, out var e))
            {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) return n;
                if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var n2)) return n2;
            }
            return 0;
        }

        bool Bool(string name, bool dflt)
            => root.TryGetProperty(name, out var e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False)
                ? e.GetBoolean() : dflt;

        return new Probe
        {
            Provider = Str("provider"),
            Host = Str("host"),
            Port = Int("port"),
            Database = Str("database"),
            Username = Str("username", "userName"),
            Password = Str("password"),
            TargetResourceGroup = Str("targetResourceGroup"),
            Encrypt = Bool("encrypt", false),
            TrustServerCertificate = Bool("trustServerCertificate", true),
        };
    }

    /// <summary>按 provider 建未打开的连接;调用方负责 OpenAsync + Dispose。connectTimeoutSeconds:连接测试 8s、ETL 给更长。</summary>
    public static DbConnection Create(
        Probe p,
        int connectTimeoutSeconds = 8,
        bool isolateMySqlPhysicalSession = false,
        int mysqlHardCommandTimeoutSeconds = 30)
    {
        if (isolateMySqlPhysicalSession && mysqlHardCommandTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mysqlHardCommandTimeoutSeconds));
        }

        switch (p.NormalizedProvider)
        {
            case "sqlserver":
            case "mssql":
                var sb = new SqlConnectionStringBuilder
                {
                    DataSource = p.Port > 0 ? $"{p.Host},{p.Port}" : p.Host,
                    InitialCatalog = p.Database ?? string.Empty,
                    ConnectTimeout = connectTimeoutSeconds,
                };
                sb["Encrypt"] = p.Encrypt;
                sb["TrustServerCertificate"] = p.TrustServerCertificate;
                if (!string.IsNullOrWhiteSpace(p.Username)) sb.UserID = p.Username;
                if (!string.IsNullOrWhiteSpace(p.Password)) sb.Password = p.Password;
                return new SqlConnection(sb.ConnectionString);

            case "mysql":
            case "mariadb":
                var mb = new MySqlConnectionStringBuilder
                {
                    Server = p.Host,
                    Port = (uint)(p.Port > 0 ? p.Port : 3306),
                    Database = p.Database ?? string.Empty,
                    ConnectionTimeout = (uint)connectTimeoutSeconds,
                    SslMode = MySqlSslMode.Preferred,
                    AllowPublicKeyRetrieval = true,
                    TreatTinyAsBoolean = false,
                    GuidFormat = MySqlGuidFormat.None,
                    DateTimeKind = MySqlDateTimeKind.Unspecified,
                    NoBackslashEscapes = false,
                    // 目标写会话持有 GET_LOCK，绝不能与业务查询共池。异常清理时，
                    // Dispose 内部回滚达到 DefaultCommandTimeout 后由
                    // CancellationTimeout=-1 直接关闭本地 socket，释放该物理会话。
                    Pooling = !isolateMySqlPhysicalSession,
                    CancellationTimeout = isolateMySqlPhysicalSession ? -1 : 2,
                    DefaultCommandTimeout = isolateMySqlPhysicalSession
                        ? checked((uint)mysqlHardCommandTimeoutSeconds)
                        : 30,
                };
                if (!string.IsNullOrWhiteSpace(p.Username)) mb.UserID = p.Username;
                if (!string.IsNullOrWhiteSpace(p.Password)) mb.Password = p.Password;
                return new MySqlConnection(mb.ConnectionString);

            case "postgresql":
            case "postgres":
            case "npgsql":
                var pb = new NpgsqlConnectionStringBuilder
                {
                    Host = p.Host,
                    Port = p.Port > 0 ? p.Port : 5432,
                    Database = p.Database ?? string.Empty,
                    Timeout = connectTimeoutSeconds,
                    CommandTimeout = isolateMySqlPhysicalSession
                        ? mysqlHardCommandTimeoutSeconds
                        : 30,
                    SslMode = p.Encrypt
                        ? (p.TrustServerCertificate ? SslMode.Require : SslMode.VerifyFull)
                        : SslMode.Prefer,
                    Pooling = !isolateMySqlPhysicalSession,
                };
                if (!string.IsNullOrWhiteSpace(p.Username)) pb.Username = p.Username;
                if (!string.IsNullOrWhiteSpace(p.Password)) pb.Password = p.Password;
                return new NpgsqlConnection(pb.ConnectionString);

            default:
                throw new NotSupportedException($"暂不支持「{p.NormalizedProvider}」,目前支持 sqlserver / mysql / postgresql。");
        }
    }

    /// <summary>SQL 标识符引用(防注入 + 容特殊字符):SqlServer [x]、MySQL `x`、PostgreSQL "x";支持 schema.table。</summary>
    public static string QuoteIdentifier(string identifier, Probe p)
    {
        return string.Join(
            ".",
            identifier
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => QuoteIdentifierPart(part, p)));
    }

    /// <summary>
    /// 引用单个标识符。源查询列别名即使含点号也必须作为一个整体引用，不能误拆成
    /// <c>schema.column</c>。
    /// </summary>
    public static string QuoteIdentifierPart(string identifier, Probe p)
    {
        return p.NormalizedProvider switch
        {
            "mysql" or "mariadb" => $"`{identifier.Replace("`", "``")}`",
            "postgresql" or "postgres" or "npgsql" => $"\"{identifier.Replace("\"", "\"\"")}\"",
            _ => $"[{identifier.Replace("]", "]]")}]",
        };
    }
}
