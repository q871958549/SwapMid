using System.Collections.Generic;
using System.Text;

namespace SwapMid.DataIntegration;

internal enum EtlSqlDialect
{
    SqlServer,
    MySql,
    PostgreSql,
}

internal enum EtlSqlTokenKind
{
    Word,
    QuotedIdentifier,
    Literal,
    OpenParen,
    CloseParen,
    Comma,
    Semicolon,
    Parameter,
    Other,
}

internal readonly record struct EtlSqlToken(
    EtlSqlTokenKind Kind,
    string Text = "");

/// <summary>
/// SQL Server/MySQL 共用的 O(n) 窄词法器。它不改写 SQL，只把字符串、引用标识符与
/// 普通注释隔离出来，并对方言歧义 fail-closed。
/// </summary>
internal static class EtlSourceQueryTokenizer
{
    internal static bool TryTokenize(
        string query,
        EtlSqlDialect dialect,
        bool mysqlBackslashEscapes,
        out List<EtlSqlToken> tokens)
    {
        tokens = [];
        var parenthesisDepth = 0;

        for (var i = 0; i < query.Length;)
        {
            var current = query[i];
            if (char.IsWhiteSpace(current) || current == '\uFEFF')
            {
                i++;
                continue;
            }

            if (current == '-' &&
                i + 1 < query.Length &&
                query[i + 1] == '-' &&
                IsLineCommentStart(query, i, dialect))
            {
                i += 2;
                while (i < query.Length && query[i] is not ('\r' or '\n'))
                {
                    i++;
                }

                continue;
            }

            if (dialect == EtlSqlDialect.MySql && current == '#')
            {
                i++;
                while (i < query.Length && query[i] is not ('\r' or '\n'))
                {
                    i++;
                }

                continue;
            }

            if (current == '/' &&
                i + 1 < query.Length &&
                query[i + 1] == '*')
            {
                if (!TryConsumeBlockComment(query, ref i, dialect))
                {
                    return false;
                }

                continue;
            }

            if (current == '\'')
            {
                if (!TryConsumeDelimited(
                        query,
                        ref i,
                        '\'',
                        mysqlBackslashEscapes && dialect is EtlSqlDialect.MySql or EtlSqlDialect.PostgreSql,
                        out _))
                {
                    return false;
                }

                tokens.Add(new EtlSqlToken(EtlSqlTokenKind.Literal));
                continue;
            }

            if (current == '"')
            {
                if (!TryConsumeDelimited(
                        query,
                        ref i,
                        '"',
                        mysqlBackslashEscapes && dialect == EtlSqlDialect.MySql,
                        out var identifier))
                {
                    return false;
                }

                // QUOTED_IDENTIFIER / ANSI_QUOTES 可能改变双引号语义；无论字符串还是
                // 标识符，其中内容都不参与关键字与分号判定。
                tokens.Add(new EtlSqlToken(
                    EtlSqlTokenKind.QuotedIdentifier,
                    identifier.ToUpperInvariant()));
                continue;
            }

            if (dialect == EtlSqlDialect.PostgreSql &&
                (current == '$' ||
                 current == '&' &&
                 i + 1 < query.Length &&
                 query[i + 1] is '\'' or '"'))
            {
                // PostgreSQL dollar-quoting ($$ / $tag$)、位置参数 ($1) 以及 U&'...' / U&"..."
                // Unicode 转义字面量会改变分隔符与转义边界，统一 fail-closed。
                return false;
            }

            if (dialect == EtlSqlDialect.SqlServer && current == '[')
            {
                if (!TryConsumeDelimited(
                        query,
                        ref i,
                        ']',
                        backslashEscapes: false,
                        out var identifier))
                {
                    return false;
                }

                tokens.Add(new EtlSqlToken(
                    EtlSqlTokenKind.QuotedIdentifier,
                    identifier.ToUpperInvariant()));
                continue;
            }

            if (dialect == EtlSqlDialect.MySql && current == '`')
            {
                if (!TryConsumeDelimited(
                        query,
                        ref i,
                        '`',
                        mysqlBackslashEscapes,
                        out var identifier))
                {
                    return false;
                }

                tokens.Add(new EtlSqlToken(
                    EtlSqlTokenKind.QuotedIdentifier,
                    identifier.ToUpperInvariant()));
                continue;
            }

            if (dialect == EtlSqlDialect.SqlServer && current == '#')
            {
                // SQL Server 的 #temp / ##temp 是标识符，不能套用 MySQL # 注释规则。
                i++;
                while (i < query.Length && IsIdentifierPart(query[i], dialect))
                {
                    i++;
                }

                tokens.Add(new EtlSqlToken(EtlSqlTokenKind.QuotedIdentifier));
                continue;
            }

            if (current == '@' ||
                current == '?' ||
                current == ':' &&
                i + 1 < query.Length &&
                (query[i + 1] == '=' || IsWordStart(query[i + 1], dialect)) &&
                (i == 0 || query[i - 1] != ':'))
            {
                tokens.Add(new EtlSqlToken(EtlSqlTokenKind.Parameter));
                i++;
                while (i < query.Length && IsIdentifierPart(query[i], dialect))
                {
                    i++;
                }

                continue;
            }

            if (IsWordStart(current, dialect))
            {
                var start = i++;
                while (i < query.Length && IsIdentifierPart(query[i], dialect))
                {
                    i++;
                }

                tokens.Add(new EtlSqlToken(
                    EtlSqlTokenKind.Word,
                    query[start..i].ToUpperInvariant()));
                continue;
            }

            switch (current)
            {
                case '(':
                    parenthesisDepth++;
                    tokens.Add(new EtlSqlToken(EtlSqlTokenKind.OpenParen));
                    break;
                case ')':
                    parenthesisDepth--;
                    if (parenthesisDepth < 0)
                    {
                        return false;
                    }

                    tokens.Add(new EtlSqlToken(EtlSqlTokenKind.CloseParen));
                    break;
                case ',':
                    tokens.Add(new EtlSqlToken(EtlSqlTokenKind.Comma));
                    break;
                case ';':
                    tokens.Add(new EtlSqlToken(EtlSqlTokenKind.Semicolon));
                    break;
                default:
                    tokens.Add(new EtlSqlToken(EtlSqlTokenKind.Other));
                    break;
            }

            i++;
        }

        return parenthesisDepth == 0;
    }

    private static bool TryConsumeBlockComment(
        string query,
        ref int index,
        EtlSqlDialect dialect)
    {
        var start = index;
        if (dialect == EtlSqlDialect.MySql &&
            (index + 2 < query.Length &&
             query[index + 2] is '!' or '+' ||
             index + 3 < query.Length &&
             query[index + 2] is 'm' or 'M' &&
             query[index + 3] == '!'))
        {
            // MySQL /*!...*/、MariaDB /*M!...*/ 会执行注释体；/*+...*/ 可控制优化器。
            return false;
        }

        var depth = 1;
        index += 2;
        while (index < query.Length)
        {
            if (index + 1 < query.Length &&
                query[index] == '/' &&
                query[index + 1] == '*')
            {
                if (dialect == EtlSqlDialect.MySql)
                {
                    // MySQL 嵌套块注释行为不稳定且已弃用，采用 fail-closed。
                    return false;
                }

                depth++;
                index += 2;
                continue;
            }

            if (index + 1 < query.Length &&
                query[index] == '*' &&
                query[index + 1] == '/')
            {
                depth--;
                index += 2;
                if (depth != 0)
                {
                    continue;
                }

                if (start > 0 &&
                    index < query.Length &&
                    IsIdentifierPart(query[start - 1], dialect) &&
                    IsIdentifierPart(query[index], dialect))
                {
                    // 禁止 IN/**/TO 一类把注释嵌入 token，避免数据库版本差异造成绕过。
                    return false;
                }

                return true;
            }

            index++;
        }

        return false;
    }

    private static bool TryConsumeDelimited(
        string query,
        ref int index,
        char delimiter,
        bool backslashEscapes,
        out string value)
    {
        var builder = new StringBuilder();
        index++;
        while (index < query.Length)
        {
            if (backslashEscapes && query[index] == '\\')
            {
                if (index + 1 >= query.Length)
                {
                    value = string.Empty;
                    return false;
                }

                builder.Append(query[index + 1]);
                index += 2;
                continue;
            }

            if (query[index] != delimiter)
            {
                builder.Append(query[index]);
                index++;
                continue;
            }

            if (index + 1 < query.Length &&
                query[index + 1] == delimiter)
            {
                builder.Append(delimiter);
                index += 2;
                continue;
            }

            index++;
            value = builder.ToString();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsLineCommentStart(
        string query,
        int index,
        EtlSqlDialect dialect)
    {
        if (dialect is EtlSqlDialect.SqlServer or EtlSqlDialect.PostgreSql)
        {
            return true;
        }

        var afterDashes = index + 2;
        return afterDashes >= query.Length ||
               IsAsciiCommentWhitespace(query[afterDashes]);
    }

    private static bool IsAsciiCommentWhitespace(char value)
        => value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

    private static bool IsWordStart(char value, EtlSqlDialect dialect)
        => char.IsLetter(value) ||
           value == '_' ||
           (dialect != EtlSqlDialect.PostgreSql && value == '$');

    private static bool IsIdentifierPart(char value, EtlSqlDialect dialect)
        => char.IsLetterOrDigit(value) ||
           value == '_' ||
           (dialect != EtlSqlDialect.PostgreSql && value is '$' or '#');
}
