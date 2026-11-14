using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwapMid;

internal sealed class ChinaDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (ChinaDateTimeFormat.TryParse(value, out var dateTime))
        {
            return dateTime;
        }

        throw new JsonException($"DateTime value must use {ChinaDateTimeFormat.DefaultFormat} or an ISO 8601 compatible format.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(ChinaDateTimeFormat.Format(value));
    }
}
