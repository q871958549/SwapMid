using System;
using System.Collections.Generic;

namespace SwapMid.DataIntegration;

/// <summary>
/// 对词法 token 应用“单条、只读、非锁定、无已知外部副作用”的窄允许策略。
/// </summary>
internal static class EtlSourceQueryPolicy
{
    private static readonly HashSet<string> GenericForbiddenWords = new(StringComparer.Ordinal)
    {
        "ALTER", "ANALYZE", "BACKUP", "BEGIN", "CALL", "COMMIT", "CREATE",
        "DBCC", "DEALLOCATE", "DECLARE", "DELETE", "DENY", "DO", "DROP",
        "EXEC", "EXECUTE", "GRANT", "HANDLER", "INSERT", "KILL", "LOAD",
        "LOCK", "MERGE", "OPTIMIZE", "PREPARE", "RECONFIGURE", "RELEASE",
        "RENAME", "REPAIR", "REPLACE", "RESTORE", "REVOKE", "ROLLBACK",
        "SAVEPOINT", "SET", "TRUNCATE", "UNLOCK", "UPDATE", "USE",
    };

    private static readonly HashSet<string> MySqlForbiddenWords = new(StringComparer.Ordinal)
    {
        "BENCHMARK", "DUMPFILE", "GET_LOCK", "HIGH_PRIORITY", "LOAD_FILE",
        "MASTER_GTID_WAIT", "MASTER_POS_WAIT", "NEXTVAL", "OUTFILE",
        "RELEASE_ALL_LOCKS", "RELEASE_LOCK", "SERVICE_GET_READ_LOCKS",
        "SERVICE_GET_WRITE_LOCKS", "SERVICE_RELEASE_LOCKS", "SETVAL", "SLEEP",
        "SOURCE_POS_WAIT", "SQL_BUFFER_RESULT", "SQL_CALC_FOUND_ROWS",
        "WAIT_FOR_EXECUTED_GTID_SET",
    };

    private static readonly HashSet<string> PostgreSqlForbiddenWords = new(StringComparer.Ordinal)
    {
        "CHECKPOINT", "CLUSTER", "COMMENT", "COPY", "CURRVAL", "DBLINK",
        "DBLINK_CONNECT", "DBLINK_DISCONNECT", "DBLINK_EXEC", "DISCARD",
        "EXPLAIN", "IMPORT", "LASTVAL", "LISTEN", "LO_CREATE", "LO_CREAT",
        "LO_EXPORT", "LO_FROM_BYTEA", "LO_IMPORT", "LO_LSEEK", "LO_OPEN",
        "LO_PUT", "LO_UNLINK", "LO_WRITE", "MOVE", "NEXTVAL", "NOTIFY",
        "PG_ADVISORY_LOCK", "PG_ADVISORY_LOCK_SHARED", "PG_ADVISORY_XACT_LOCK",
        "PG_ADVISORY_XACT_LOCK_SHARED", "PG_BACKUP_START", "PG_BACKUP_STOP",
        "PG_CANCEL_BACKEND", "PG_COPY_LOGICAL_SLOT_CHANGES",
        "PG_COPY_PHYSICAL_SLOT_CHANGES", "PG_CREATE_LOGICAL_REPLICATION_SLOT",
        "PG_CREATE_PHYSICAL_REPLICATION_SLOT", "PG_CREATE_RESTORE_POINT",
        "PG_DROP_REPLICATION_SLOT", "PG_EXPORT_SNAPSHOT", "PG_IMPORT_SYSTEM_COLLATIONS",
        "PG_LOG_BACKEND_MEMORY_CONTEXTS", "PG_LS_ARCHIVE_STATUSDIR", "PG_LS_DIR",
        "PG_LS_LOGDIR", "PG_LS_TMPDIR", "PG_LS_WALDIR", "PG_NOTIFY",
        "PG_PROMOTE", "PG_READ_BINARY_FILE", "PG_READ_FILE", "PG_RELOAD_CONF",
        "PG_REPLICATION_ORIGIN_ADVANCE", "PG_ROTATE_LOGFILE", "PG_SLEEP",
        "PG_SLEEP_FOR", "PG_SLEEP_UNTIL", "PG_START_BACKUP", "PG_STAT_FILE",
        "PG_STAT_RESET", "PG_STOP_BACKUP", "PG_SWITCH_WAL", "PG_TERMINATE_BACKEND",
        "PG_TRY_ADVISORY_LOCK", "PG_TRY_ADVISORY_LOCK_SHARED",
        "PG_TRY_ADVISORY_XACT_LOCK", "PG_TRY_ADVISORY_XACT_LOCK_SHARED",
        "REASSIGN", "REFRESH", "REINDEX", "RESET", "SECURITY", "SETVAL",
        "SET_CONFIG", "SHOW", "UESCAPE", "UNLISTEN", "VACUUM",
    };

    internal static bool IsSafeSingleSelect(
        IReadOnlyList<EtlSqlToken> tokens,
        EtlSqlDialect dialect)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        var statementEnd = tokens.Count;
        var terminatorSeen = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != EtlSqlTokenKind.Semicolon)
            {
                continue;
            }

            if (terminatorSeen || i != tokens.Count - 1)
            {
                return false;
            }

            terminatorSeen = true;
            statementEnd = i;
        }

        if (statementEnd == 0 ||
            !HasReadOnlyRoot(tokens, 0, statementEnd, dialect) ||
            !HasSingleTopLevelSelectExpression(tokens, statementEnd))
        {
            return false;
        }

        for (var i = 0; i < statementEnd; i++)
        {
            var token = tokens[i];
            if (token.Kind == EtlSqlTokenKind.Parameter)
            {
                // 当前 sourceQuery 没有参数值契约；同时阻断用户/会话变量。
                return false;
            }

            if (dialect == EtlSqlDialect.MySql &&
                token.Kind == EtlSqlTokenKind.QuotedIdentifier &&
                i + 1 < statementEnd &&
                tokens[i + 1].Kind == EtlSqlTokenKind.OpenParen &&
                MySqlForbiddenWords.Contains(token.Text))
            {
                // 反引号和 ANSI_QUOTES 双引号函数名仍可被 MySQL 解析为函数调用。
                return false;
            }

            if (dialect == EtlSqlDialect.PostgreSql &&
                token.Kind == EtlSqlTokenKind.QuotedIdentifier &&
                i + 1 < statementEnd &&
                tokens[i + 1].Kind == EtlSqlTokenKind.OpenParen &&
                (PostgreSqlForbiddenWords.Contains(token.Text) ||
                 GenericForbiddenWords.Contains(token.Text)))
            {
                // PostgreSQL 双引号引用函数名（如 "pg_sleep"(1)）仍会被解析为函数调用。
                return false;
            }

            if (token.Kind != EtlSqlTokenKind.Word)
            {
                continue;
            }

            if (token.Text == "INTO" ||
                dialect is EtlSqlDialect.MySql or EtlSqlDialect.PostgreSql &&
                GenericForbiddenWords.Contains(token.Text) &&
                !IsAllowedReadOnlyFunction(tokens, i, statementEnd, dialect) ||
                dialect == EtlSqlDialect.MySql &&
                MySqlForbiddenWords.Contains(token.Text) ||
                dialect == EtlSqlDialect.PostgreSql &&
                PostgreSqlForbiddenWords.Contains(token.Text))
            {
                return false;
            }
        }

        if (ContainsWordSequence(tokens, statementEnd, "NEXT", "VALUE", "FOR"))
        {
            // SELECT NEXT VALUE FOR 会推进序列，虽以 SELECT 开头仍有持久副作用。
            return false;
        }

        if (dialect == EtlSqlDialect.MySql &&
            (ContainsWordSequence(tokens, statementEnd, "FOR", "UPDATE") ||
             ContainsWordSequence(tokens, statementEnd, "FOR", "SHARE") ||
             ContainsWordSequence(tokens, statementEnd, "LOCK", "IN", "SHARE", "MODE")))
        {
            return false;
        }

        if (dialect == EtlSqlDialect.PostgreSql &&
            (ContainsWordSequence(tokens, statementEnd, "FOR", "UPDATE") ||
             ContainsWordSequence(tokens, statementEnd, "FOR", "NO", "KEY", "UPDATE") ||
             ContainsWordSequence(tokens, statementEnd, "FOR", "SHARE") ||
             ContainsWordSequence(tokens, statementEnd, "FOR", "KEY", "SHARE")))
        {
            return false;
        }

        return true;
    }

    private static bool IsAllowedReadOnlyFunction(
        IReadOnlyList<EtlSqlToken> tokens,
        int index,
        int end,
        EtlSqlDialect dialect)
    {
        if (index + 1 >= end ||
            tokens[index + 1].Kind != EtlSqlTokenKind.OpenParen)
        {
            return false;
        }

        return tokens[index].Text == "REPLACE" ||
               dialect == EtlSqlDialect.MySql &&
               tokens[index].Text == "INSERT";
    }

    private static bool HasSingleTopLevelSelectExpression(
        IReadOnlyList<EtlSqlToken> tokens,
        int end)
    {
        var depth = 0;
        var topLevelWords = new List<string>();
        var topLevelSelectCount = 0;
        for (var i = 0; i < end; i++)
        {
            var token = tokens[i];
            if (token.Kind == EtlSqlTokenKind.OpenParen)
            {
                depth++;
                continue;
            }

            if (token.Kind == EtlSqlTokenKind.CloseParen)
            {
                depth--;
                continue;
            }

            if (depth != 0 || token.Kind != EtlSqlTokenKind.Word)
            {
                continue;
            }

            if (token.Text == "SELECT")
            {
                topLevelSelectCount++;
                if (topLevelSelectCount > 1 &&
                    !IsSetOperatorContinuation(topLevelWords))
                {
                    // 两种方言都允许省略分号；非集合运算的第二个顶层 SELECT
                    // 就是第二条语句，不能靠分号计数识别。
                    return false;
                }
            }

            topLevelWords.Add(token.Text);
        }

        return topLevelSelectCount > 0;
    }

    private static bool IsSetOperatorContinuation(
        IReadOnlyList<string> topLevelWords)
    {
        if (topLevelWords.Count == 0)
        {
            return false;
        }

        var previous = topLevelWords[^1];
        if (previous is "UNION" or "INTERSECT" or "EXCEPT")
        {
            return true;
        }

        return previous is "ALL" or "DISTINCT" &&
               topLevelWords.Count >= 2 &&
               topLevelWords[^2] is "UNION" or "INTERSECT" or "EXCEPT";
    }

    private static bool HasReadOnlyRoot(
        IReadOnlyList<EtlSqlToken> tokens,
        int start,
        int end,
        EtlSqlDialect dialect)
    {
        if (start >= end || tokens[start].Kind != EtlSqlTokenKind.Word)
        {
            return false;
        }

        if (tokens[start].Text == "SELECT")
        {
            return true;
        }

        if (tokens[start].Text != "WITH")
        {
            return false;
        }

        var index = start + 1;
        if (dialect is EtlSqlDialect.MySql or EtlSqlDialect.PostgreSql &&
            index < end &&
            IsWord(tokens[index], "RECURSIVE"))
        {
            index++;
        }

        while (index < end)
        {
            if (!IsIdentifier(tokens[index]))
            {
                return false;
            }

            index++;
            if (index < end &&
                tokens[index].Kind == EtlSqlTokenKind.OpenParen &&
                !TrySkipParenthesized(tokens, ref index, end))
            {
                return false;
            }

            if (index >= end || !IsWord(tokens[index], "AS"))
            {
                return false;
            }

            index++;
            if (index >= end ||
                tokens[index].Kind != EtlSqlTokenKind.OpenParen)
            {
                return false;
            }

            var bodyStart = index + 1;
            if (!TrySkipParenthesized(tokens, ref index, end))
            {
                return false;
            }

            var bodyEnd = index - 1;
            if (bodyStart >= bodyEnd ||
                !IsWord(tokens[bodyStart], "SELECT"))
            {
                return false;
            }

            if (index < end &&
                tokens[index].Kind == EtlSqlTokenKind.Comma)
            {
                index++;
                continue;
            }

            return index < end && IsWord(tokens[index], "SELECT");
        }

        return false;
    }

    private static bool TrySkipParenthesized(
        IReadOnlyList<EtlSqlToken> tokens,
        ref int index,
        int end)
    {
        if (index >= end ||
            tokens[index].Kind != EtlSqlTokenKind.OpenParen)
        {
            return false;
        }

        var depth = 0;
        for (; index < end; index++)
        {
            if (tokens[index].Kind == EtlSqlTokenKind.OpenParen)
            {
                depth++;
            }
            else if (tokens[index].Kind == EtlSqlTokenKind.CloseParen)
            {
                depth--;
                if (depth == 0)
                {
                    index++;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsWordSequence(
        IReadOnlyList<EtlSqlToken> tokens,
        int end,
        params string[] words)
    {
        for (var i = 0; i <= end - words.Length; i++)
        {
            var matches = true;
            for (var j = 0; j < words.Length; j++)
            {
                if (!IsWord(tokens[i + j], words[j]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifier(EtlSqlToken token)
        => token.Kind is EtlSqlTokenKind.Word or
            EtlSqlTokenKind.QuotedIdentifier;

    private static bool IsWord(EtlSqlToken token, string expected)
        => token.Kind == EtlSqlTokenKind.Word &&
           string.Equals(token.Text, expected, StringComparison.Ordinal);
}
