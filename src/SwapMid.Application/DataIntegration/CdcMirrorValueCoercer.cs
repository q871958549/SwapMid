using System;
using System.Globalization;

namespace SwapMid.DataIntegration;

/// <summary>
/// 将 PostgreSQL <c>pgoutput</c> 文本模式字段值及快照字符串值精确解析为目标表列对应的 CLR 类型，
/// 避免 Dapper 参数绑定向 PostgreSQL / SQL Server / MySQL 暂存表插入时因 <c>text</c> 隐式类型冲突失败。
/// </summary>
internal static class CdcMirrorValueCoercer
{
    internal static object? Coerce(object? value, string? targetDataType)
    {
        if (value == null || string.IsNullOrWhiteSpace(targetDataType))
        {
            return value;
        }

        if (value is not string s)
        {
            return value;
        }

        var dt = targetDataType.Trim().ToLowerInvariant();
        if (dt is "bigint" or "int8" &&
            long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i64))
        {
            return i64;
        }

        if (dt is "integer" or "int" or "int4" or "mediumint" &&
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32))
        {
            return i32;
        }

        if (dt is "smallint" or "int2" or "tinyint" &&
            short.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i16))
        {
            return i16;
        }

        if ((dt.StartsWith("numeric", StringComparison.Ordinal) ||
             dt.StartsWith("decimal", StringComparison.Ordinal) ||
             dt is "money" or "smallmoney") &&
            decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec))
        {
            return dec;
        }

        if ((dt is "double precision" or "float8" or "double" or "float") &&
            double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dbl))
        {
            return dbl;
        }

        if ((dt is "real" or "float4") &&
            float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var flt))
        {
            return flt;
        }

        if (dt is "boolean" or "bool" or "bit")
        {
            if (s is "t" or "T" or "1") return true;
            if (s is "f" or "F" or "0") return false;
            if (bool.TryParse(s, out var b)) return b;
        }

        if (dt is "uuid" or "uniqueidentifier" &&
            Guid.TryParse(s, out var guid))
        {
            return guid;
        }

        if (dt == "date" &&
            DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dOnly))
        {
            return dOnly;
        }

        if (dt.StartsWith("timestamp with time zone", StringComparison.Ordinal) ||
            dt is "timestamptz" or "datetimeoffset")
        {
            if (DateTimeOffset.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out var dto))
            {
                if (dt == "datetimeoffset")
                {
                    return dto;
                }

                return dto.UtcDateTime;
            }
        }

        if (dt.StartsWith("timestamp", StringComparison.Ordinal) ||
            dt is "datetime" or "datetime2" or "smalldatetime")
        {
            if (DateTime.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsedDt))
            {
                return DateTime.SpecifyKind(parsedDt, DateTimeKind.Unspecified);
            }
        }

        if (dt.StartsWith("time", StringComparison.Ordinal) &&
            !dt.StartsWith("timestamp", StringComparison.Ordinal) &&
            TimeOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tOnly))
        {
            return tOnly;
        }

        if (dt is "bytea" or "varbinary" or "binary" or "blob" or "longblob")
        {
            if (s.StartsWith("\\x", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Convert.FromHexString(s.AsSpan(2));
                }
                catch (FormatException)
                {
                    return s;
                }
            }

            try
            {
                return Convert.FromBase64String(s);
            }
            catch (FormatException)
            {
                return s;
            }
        }

        return value;
    }
}
