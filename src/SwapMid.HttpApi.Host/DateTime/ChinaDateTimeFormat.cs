using System;
using System.Globalization;

namespace SwapMid;

internal static class ChinaDateTimeFormat
{
    public const string DefaultFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] LocalFormats =
    [
        DefaultFormat,
        "yyyy-MM-dd HH:mm",
        "yyyy/M/d H:mm:ss",
        "yyyy/M/d H:mm"
    ];

    private static readonly TimeZoneInfo ChinaTimeZone = ResolveChinaTimeZone();

    public static bool TryParse(string? value, out DateTime utcDateTime)
    {
        utcDateTime = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (DateTime.TryParseExact(trimmed, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            utcDateTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ChinaTimeZone);
            return true;
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset))
        {
            utcDateTime = dateTimeOffset.UtcDateTime;
            return true;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out local))
        {
            utcDateTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ChinaTimeZone);
            return true;
        }

        return false;
    }

    public static string Format(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, ChinaTimeZone)
            .ToString(DefaultFormat, CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }
}
