using System.Text.Json;
using System.Text.Json.Serialization;

namespace VidroApi.Application.Common.Logging.Converters;

public sealed class MaskingStringConverter(int keepFirst, int keepLast, char maskChar) : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString();

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteStringValue(value);
            return;
        }

        var first   = Math.Clamp(keepFirst, 0, value.Length);
        var last    = Math.Clamp(keepLast, 0, value.Length - first);
        var masked  = Math.Max(0, value.Length - first - last);

        var result = string.Concat(
            value.AsSpan(0, first),
            new string(maskChar, masked),
            value.AsSpan(value.Length - last, last));

        writer.WriteStringValue(result);
    }
}
