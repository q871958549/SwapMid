using System;
using System.Text;

namespace SwapMid.DataIntegration;

/// <summary>
/// MySQL/MariaDB <c>SHOW CREATE TABLE</c> 的窄解析器。只处理 ETL 需要证明的
/// 顶层 table option 与临时表不支持的结构；引用字符串/标识符始终作为不可解释原子。
/// </summary>
internal static class EtlMySqlCreateDefinition
{
    internal static string NormalizeForFingerprint(
        string definition,
        bool backslashEscapes)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        var normalized = new StringBuilder(definition.Length);
        var parenthesisDepth = 0;
        for (var index = 0; index < definition.Length;)
        {
            var current = definition[index];
            if (IsQuote(current))
            {
                CopyQuoted(
                    definition,
                    normalized,
                    ref index,
                    current,
                    backslashEscapes);
                continue;
            }

            if (current == '(')
            {
                parenthesisDepth++;
            }
            else if (current == ')')
            {
                parenthesisDepth--;
                if (parenthesisDepth < 0)
                {
                    throw EtlPipelineRunner.InvalidInput();
                }
            }

            if (parenthesisDepth == 0 &&
                TryFindAutoIncrementCounter(
                    definition,
                    index,
                    out var digitStart,
                    out var digitEnd))
            {
                normalized.Append(
                    definition,
                    index,
                    digitStart - index);
                // AUTO_INCREMENT 的当前计数是数据态，不是 DDL。保留选项本身，只把
                // 未引用、顶层的数字归一化；CHECK/COMMENT/约束名中的相同文本不得触碰。
                normalized.Append('0');
                index = digitEnd;
                continue;
            }

            normalized.Append(current);
            index++;
        }

        if (parenthesisDepth != 0)
        {
            throw EtlPipelineRunner.InvalidInput();
        }

        return normalized.ToString();
    }

    internal static bool HasUnsupportedTemporaryCloneShape(
        string definition,
        bool backslashEscapes)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return true;
        }

        var parenthesisDepth = 0;
        for (var index = 0; index < definition.Length;)
        {
            var current = definition[index];
            if (IsQuote(current))
            {
                SkipQuoted(
                    definition,
                    ref index,
                    current,
                    backslashEscapes);
                continue;
            }

            if (current == '(')
            {
                parenthesisDepth++;
                index++;
                continue;
            }

            if (current == ')')
            {
                parenthesisDepth--;
                if (parenthesisDepth < 0)
                {
                    return true;
                }

                index++;
                continue;
            }

            if (!IsWordStart(current))
            {
                index++;
                continue;
            }

            var wordStart = index++;
            while (index < definition.Length &&
                   IsWordPart(definition[index]))
            {
                index++;
            }

            var word = definition[wordStart..index];
            if (word.Equals(
                    "FULLTEXT",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (parenthesisDepth != 0)
            {
                continue;
            }

            if (word.Equals(
                    "TABLESPACE",
                    StringComparison.OrdinalIgnoreCase) ||
                word.Equals(
                    "KEY_BLOCK_SIZE",
                    StringComparison.OrdinalIgnoreCase))
            {
                // system/general tablespace 及压缩 key block 不能克隆成 InnoDB
                // session temporary table；显式 file-per-table 也已弃用，统一拒绝。
                return true;
            }

            if (word.Equals(
                    "ROW_FORMAT",
                    StringComparison.OrdinalIgnoreCase) &&
                TryReadOptionWord(
                    definition,
                    index,
                    out var optionValue) &&
                optionValue.Equals(
                    "COMPRESSED",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return parenthesisDepth != 0;
    }

    private static bool TryFindAutoIncrementCounter(
        string definition,
        int index,
        out int digitStart,
        out int digitEnd)
    {
        digitStart = 0;
        digitEnd = 0;
        const string option = "AUTO_INCREMENT";
        if (!MatchesWord(definition, index, option))
        {
            return false;
        }

        var cursor = index + option.Length;
        SkipWhitespace(definition, ref cursor);
        if (cursor >= definition.Length ||
            definition[cursor] != '=')
        {
            return false;
        }

        cursor++;
        SkipWhitespace(definition, ref cursor);
        digitStart = cursor;
        while (cursor < definition.Length &&
               char.IsAsciiDigit(definition[cursor]))
        {
            cursor++;
        }

        if (cursor == digitStart ||
            cursor < definition.Length &&
            IsWordPart(definition[cursor]))
        {
            return false;
        }

        digitEnd = cursor;
        return true;
    }

    private static bool TryReadOptionWord(
        string definition,
        int index,
        out string value)
    {
        SkipWhitespace(definition, ref index);
        if (index < definition.Length &&
            definition[index] == '=')
        {
            index++;
            SkipWhitespace(definition, ref index);
        }

        if (index >= definition.Length ||
            !IsWordStart(definition[index]))
        {
            value = string.Empty;
            return false;
        }

        var start = index++;
        while (index < definition.Length &&
               IsWordPart(definition[index]))
        {
            index++;
        }

        value = definition[start..index];
        return true;
    }

    private static bool MatchesWord(
        string value,
        int index,
        string expected)
    {
        if (index < 0 ||
            index + expected.Length > value.Length ||
            index > 0 && IsWordPart(value[index - 1]) ||
            !value.AsSpan(index, expected.Length)
                .Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var end = index + expected.Length;
        return end == value.Length || !IsWordPart(value[end]);
    }

    private static void CopyQuoted(
        string value,
        StringBuilder output,
        ref int index,
        char quote,
        bool backslashEscapes)
    {
        var start = index;
        SkipQuoted(
            value,
            ref index,
            quote,
            backslashEscapes);
        output.Append(value, start, index - start);
    }

    private static void SkipQuoted(
        string value,
        ref int index,
        char quote,
        bool backslashEscapes)
    {
        index++;
        while (index < value.Length)
        {
            if (quote != '`' &&
                backslashEscapes &&
                value[index] == '\\')
            {
                if (index + 1 >= value.Length)
                {
                    throw EtlPipelineRunner.InvalidInput();
                }

                index += 2;
                continue;
            }

            if (value[index] != quote)
            {
                index++;
                continue;
            }

            if (index + 1 < value.Length &&
                value[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            index++;
            return;
        }

        throw EtlPipelineRunner.InvalidInput();
    }

    private static void SkipWhitespace(
        string value,
        ref int index)
    {
        while (index < value.Length &&
               char.IsWhiteSpace(value[index]))
        {
            index++;
        }
    }

    private static bool IsQuote(char value) =>
        value is '\'' or '"' or '`';

    private static bool IsWordStart(char value) =>
        char.IsLetter(value) || value is '_' or '$';

    private static bool IsWordPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$';
}
