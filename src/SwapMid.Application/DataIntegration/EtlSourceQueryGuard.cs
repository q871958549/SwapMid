using System;
using Volo.Abp;

namespace SwapMid.DataIntegration;

/// <summary>
/// ETL 源查询的窄语法安全边界：只接受单条只读 SELECT（含以 SELECT 收口的 CTE）。
/// 这是词法纵深防御，不替代源库只读账号；校验失败不得携带 SQL 原文或判定细节。
/// </summary>
internal static class EtlSourceQueryGuard
{
    private const int MaxSourceQueryLength = 64 * 1024;

    /// <summary>
    /// 验证源查询是当前 provider 方言下的一条非锁定、无外部副作用 SELECT。
    /// </summary>
    internal static void Validate(string sourceQuery, string normalizedProvider)
    {
        if (string.IsNullOrWhiteSpace(sourceQuery) ||
            sourceQuery.Length > MaxSourceQueryLength)
        {
            throw InvalidInput();
        }

        var dialect = normalizedProvider?.Trim().ToLowerInvariant() switch
        {
            "sqlserver" or "mssql" => EtlSqlDialect.SqlServer,
            "mysql" or "mariadb" => EtlSqlDialect.MySql,
            "postgresql" or "postgres" or "npgsql" => EtlSqlDialect.PostgreSql,
            _ => throw InvalidInput(),
        };

        if (dialect == EtlSqlDialect.SqlServer)
        {
            if (!EtlSqlServerSourceQueryGuard.IsSafeSingleSelect(sourceQuery) ||
                !EtlSourceQueryTokenizer.TryTokenize(
                    sourceQuery,
                    dialect,
                    mysqlBackslashEscapes: false,
                    out var tokens) ||
                !EtlSourceQueryPolicy.IsSafeSingleSelect(tokens, dialect))
            {
                throw InvalidInput();
            }

            return;
        }

        // MySQL 的 NO_BACKSLASH_ESCAPES / ANSI_QUOTES 由服务器会话决定，配置中并无可靠事实源。
        // 两种反斜杠解释都必须得到安全结论，避免词法器与服务器模式不一致造成假阴性。
        if (!EtlSourceQueryTokenizer.TryTokenize(
                sourceQuery,
                dialect,
                mysqlBackslashEscapes: true,
                out var defaultModeTokens) ||
            !EtlSourceQueryPolicy.IsSafeSingleSelect(defaultModeTokens, dialect) ||
            !EtlSourceQueryTokenizer.TryTokenize(
                sourceQuery,
                dialect,
                mysqlBackslashEscapes: false,
                out var noBackslashTokens) ||
            !EtlSourceQueryPolicy.IsSafeSingleSelect(noBackslashTokens, dialect))
        {
            throw InvalidInput();
        }
    }

    private static BusinessException InvalidInput()
        => new(SwapMidDomainErrorCodes.DataIntegration.PipelineExecutionInputInvalid);
}
