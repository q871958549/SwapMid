using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using SwapMid.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SwapMid.DeveloperDocs;

/// <summary>
/// 外部关系库 Schema 只读读取器：配置驱动（<c>DeveloperDocs:ExternalDbSources</c>），支持 SQL Server / PostgreSQL / MySQL。
/// </summary>
public interface IExternalDbSchemaReader
{
    IReadOnlyList<ExternalDbSourceDto> GetSources();

    Task<IReadOnlyList<SchemaTableInfo>> ReadTablesAsync(string sourceKey, string? filter = null);

    Task<SchemaTableInfo?> ReadTableAsync(string sourceKey, string tableName);
}

public class ExternalDbSchemaReader : IExternalDbSchemaReader, ITransientDependency
{
    public const int MaxTables = 200;

    private readonly IConfiguration _configuration;
    private readonly IExternalDbConnectionFactory _connectionFactory;
    private readonly ILogger<ExternalDbSchemaReader> _logger;

    public ExternalDbSchemaReader(
        IConfiguration configuration,
        IExternalDbConnectionFactory connectionFactory,
        ILogger<ExternalDbSchemaReader> logger)
    {
        _configuration = configuration;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public IReadOnlyList<ExternalDbSourceDto> GetSources()
    {
        var result = new List<ExternalDbSourceDto>();
        foreach (var section in _configuration.GetSection("DeveloperDocs:ExternalDbSources").GetChildren())
        {
            var key = section["Key"]?.Trim();
            var displayName = section["DisplayName"]?.Trim();
            var dialect = NormalizeDialect(section["Dialect"] ?? section["Provider"]);
            if (string.IsNullOrWhiteSpace(key) || dialect is null)
            {
                continue;
            }

            result.Add(new ExternalDbSourceDto
            {
                Key = key,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName,
                Dialect = dialect,
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<SchemaTableInfo>> ReadTablesAsync(string sourceKey, string? filter = null)
    {
        var (source, connStr) = Resolve(sourceKey);
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return Array.Empty<SchemaTableInfo>();
        }

        var keyword = filter?.Trim() ?? string.Empty;
        try
        {
            var tables = source.Dialect switch
            {
                "SqlServer" => await ReadSqlServerTablesAsync(source, connStr, keyword),
                "PostgreSql" => await ReadPostgreSqlTablesAsync(source, connStr, keyword),
                "MySql" => await ReadMySqlTablesAsync(source, connStr, keyword),
                _ => Array.Empty<SchemaTableInfo>(),
            };
            return Truncate(tables, sourceKey, keyword);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取外部库 {Source} 表清单失败，返回空集", sourceKey);
            return Array.Empty<SchemaTableInfo>();
        }
    }

    public async Task<SchemaTableInfo?> ReadTableAsync(string sourceKey, string tableName)
    {
        var (source, connStr) = Resolve(sourceKey);
        if (string.IsNullOrWhiteSpace(connStr))
        {
            return null;
        }

        try
        {
            return source.Dialect switch
            {
                "SqlServer" => await ReadSqlServerTableAsync(source, connStr, tableName),
                "PostgreSql" => await ReadPostgreSqlTableAsync(source, connStr, tableName),
                "MySql" => await ReadMySqlTableAsync(source, connStr, tableName),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取外部库 {Source} 表 {Table} 失败", sourceKey, tableName);
            return null;
        }
    }

    private (ExternalDbSourceDto Source, string? ConnectionString) Resolve(string sourceKey)
    {
        foreach (var section in _configuration.GetSection("DeveloperDocs:ExternalDbSources").GetChildren())
        {
            var key = section["Key"]?.Trim();
            if (!string.Equals(key, sourceKey, StringComparison.Ordinal))
            {
                continue;
            }

            var dialect = NormalizeDialect(section["Dialect"] ?? section["Provider"])
                          ?? throw new UserFriendlyException($"外部数据源「{sourceKey}」方言不受支持");
            var displayName = section["DisplayName"]?.Trim();
            var connName = string.IsNullOrWhiteSpace(section["ConnectionStringName"])
                ? key!
                : section["ConnectionStringName"]!.Trim();
            var connStr = section["ConnectionString"];
            if (string.IsNullOrWhiteSpace(connStr))
            {
                connStr = _configuration.GetConnectionString(connName);
            }

            return (
                new ExternalDbSourceDto
                {
                    Key = key!,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? key! : displayName,
                    Dialect = dialect,
                },
                connStr);
        }

        throw new UserFriendlyException($"未知的外部数据源「{sourceKey}」");
    }

    private static string? NormalizeDialect(string? raw)
    {
        return raw?.Trim().ToLowerInvariant() switch
        {
            "sqlserver" or "mssql" => "SqlServer",
            "postgresql" or "postgres" or "npgsql" => "PostgreSql",
            "mysql" or "mariadb" => "MySql",
            _ => null,
        };
    }

    private IReadOnlyList<SchemaTableInfo> Truncate(
        IReadOnlyList<SchemaTableInfo> tables,
        string sourceKey,
        string keyword)
    {
        if (tables.Count <= MaxTables) return tables;

        _logger.LogWarning(
            "外部库 {Source} 表清单命中超过 {Max} 张（关键词「{Filter}」），已截断",
            sourceKey,
            MaxTables,
            keyword);
        return tables.Take(MaxTables).ToList();
    }

    internal const string SqlServerTablesSql = @"
SELECT TOP (@limitPlusOne)
       t.name AS Name,
       CAST(ep.value AS NVARCHAR(4000)) AS Description
FROM sys.tables t
LEFT JOIN sys.extended_properties ep
       ON ep.major_id = t.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description' AND ep.class = 1
WHERE t.is_ms_shipped = 0
  AND (@filter = ''
       OR CHARINDEX(@filter, t.name) > 0
       OR CHARINDEX(@filter, CAST(ep.value AS NVARCHAR(4000))) > 0)
ORDER BY t.name;";

    private async Task<IReadOnlyList<SchemaTableInfo>> ReadSqlServerTablesAsync(
        ExternalDbSourceDto source,
        string connStr,
        string keyword)
    {
        await using var lease = await _connectionFactory.OpenSqlServerConnectionAsync(connStr, $"schema:{source.Key}");
        var tables = (await lease.Connection.QueryAsync<SchemaTableInfo>(
            SqlServerTablesSql,
            new { filter = keyword, limitPlusOne = MaxTables + 1 })).ToList();
        Decorate(tables, source);
        return tables;
    }

    private async Task<SchemaTableInfo?> ReadSqlServerTableAsync(
        ExternalDbSourceDto source,
        string connStr,
        string tableName)
    {
        const string sql = @"
SELECT c.name AS Name,
       ty.name + CASE WHEN ty.name IN ('varchar','char','binary')
                      THEN '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length AS VARCHAR) END + ')'
                      WHEN ty.name IN ('nvarchar','nchar')
                      THEN '(' + CASE WHEN c.max_length = -1 THEN 'max' ELSE CAST(c.max_length/2 AS VARCHAR) END + ')'
                      ELSE '' END AS StoreType,
       ty.name AS ClrType,
       c.is_nullable AS IsNullable,
       CASE WHEN ic.column_id IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
       CAST(ep.value AS NVARCHAR(4000)) AS Comment
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.indexes pk ON pk.object_id = t.object_id AND pk.is_primary_key = 1
LEFT JOIN sys.index_columns ic ON ic.object_id = t.object_id AND ic.index_id = pk.index_id AND ic.column_id = c.column_id
LEFT JOIN sys.extended_properties ep ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description'
WHERE t.name = @tableName
ORDER BY c.column_id;";
        await using var lease = await _connectionFactory.OpenSqlServerConnectionAsync(connStr, $"schema:{source.Key}");
        var cols = (await lease.Connection.QueryAsync<SchemaColumnInfo>(sql, new { tableName })).ToList();
        return BuildTable(cols, source, tableName);
    }

    internal const string PostgreSqlTablesSql = @"
SELECT c.relname AS ""Name"",
       obj_description(c.oid) AS ""Description""
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind = 'r' AND n.nspname = 'public'
  AND (@filter = ''
       OR position(lower(@filter) IN lower(c.relname)) > 0
       OR position(lower(@filter) IN lower(COALESCE(obj_description(c.oid), ''))) > 0)
ORDER BY c.relname
LIMIT @limitPlusOne;";

    private async Task<IReadOnlyList<SchemaTableInfo>> ReadPostgreSqlTablesAsync(
        ExternalDbSourceDto source,
        string connStr,
        string keyword)
    {
        await using var lease = await _connectionFactory.OpenPostgreSqlConnectionAsync(connStr, $"schema:{source.Key}");
        var tables = (await lease.Connection.QueryAsync<SchemaTableInfo>(
            PostgreSqlTablesSql,
            new { filter = keyword, limitPlusOne = MaxTables + 1 })).ToList();
        Decorate(tables, source);
        return tables;
    }

    private async Task<SchemaTableInfo?> ReadPostgreSqlTableAsync(
        ExternalDbSourceDto source,
        string connStr,
        string tableName)
    {
        const string sql = @"
SELECT a.attname AS ""Name"",
       format_type(a.atttypid, a.atttypmod) AS ""StoreType"",
       format_type(a.atttypid, a.atttypmod) AS ""ClrType"",
       NOT a.attnotnull AS ""IsNullable"",
       COALESCE(pk.is_pk, false) AS ""IsPrimaryKey"",
       col_description(a.attrelid, a.attnum) AS ""Comment""
FROM pg_attribute a
JOIN pg_class c ON c.oid = a.attrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
LEFT JOIN (
    SELECT a2.attname, true AS is_pk
    FROM pg_index i
    JOIN pg_attribute a2 ON a2.attrelid = i.indrelid AND a2.attnum = ANY (i.indkey)
    WHERE i.indrelid = (quote_ident(@tableName))::regclass AND i.indisprimary
) pk ON pk.attname = a.attname
WHERE c.relname = @tableName AND n.nspname = 'public' AND a.attnum > 0 AND NOT a.attisdropped
ORDER BY a.attnum;";
        await using var lease = await _connectionFactory.OpenPostgreSqlConnectionAsync(connStr, $"schema:{source.Key}");
        var cols = (await lease.Connection.QueryAsync<SchemaColumnInfo>(sql, new { tableName })).ToList();
        return BuildTable(cols, source, tableName);
    }

    internal const string MySqlTablesSql = @"
SELECT TABLE_NAME AS Name,
       TABLE_COMMENT AS Description
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_TYPE = 'BASE TABLE'
  AND (@filter = ''
       OR LOCATE(LOWER(@filter), LOWER(TABLE_NAME)) > 0
       OR LOCATE(LOWER(@filter), LOWER(COALESCE(TABLE_COMMENT, ''))) > 0)
ORDER BY TABLE_NAME
LIMIT @limitPlusOne;";

    private async Task<IReadOnlyList<SchemaTableInfo>> ReadMySqlTablesAsync(
        ExternalDbSourceDto source,
        string connStr,
        string keyword)
    {
        await using var lease = await _connectionFactory.OpenMySqlConnectionAsync(connStr, $"schema:{source.Key}");
        var tables = (await lease.Connection.QueryAsync<SchemaTableInfo>(
            MySqlTablesSql,
            new { filter = keyword, limitPlusOne = MaxTables + 1 })).ToList();
        Decorate(tables, source);
        return tables;
    }

    private async Task<SchemaTableInfo?> ReadMySqlTableAsync(
        ExternalDbSourceDto source,
        string connStr,
        string tableName)
    {
        const string sql = @"
SELECT COLUMN_NAME AS Name,
       COLUMN_TYPE AS StoreType,
       DATA_TYPE AS ClrType,
       CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
       CASE WHEN COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END AS IsPrimaryKey,
       COLUMN_COMMENT AS Comment
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = @tableName
ORDER BY ORDINAL_POSITION;";
        await using var lease = await _connectionFactory.OpenMySqlConnectionAsync(connStr, $"schema:{source.Key}");
        var cols = (await lease.Connection.QueryAsync<SchemaColumnInfo>(sql, new { tableName })).ToList();
        return BuildTable(cols, source, tableName);
    }

    private static void Decorate(List<SchemaTableInfo> tables, ExternalDbSourceDto source)
    {
        foreach (var t in tables)
        {
            t.ClrTypeName = t.Name;
            t.DisplayName = string.IsNullOrEmpty(t.Description) ? t.Name : t.Description!;
            t.DomainGroup = source.DisplayName;
        }
    }

    private static SchemaTableInfo? BuildTable(List<SchemaColumnInfo> cols, ExternalDbSourceDto source, string tableName)
    {
        if (cols.Count == 0) return null;
        return new SchemaTableInfo
        {
            Name = tableName,
            ClrTypeName = tableName,
            DisplayName = tableName,
            DomainGroup = source.DisplayName,
            Columns = cols,
        };
    }
}
