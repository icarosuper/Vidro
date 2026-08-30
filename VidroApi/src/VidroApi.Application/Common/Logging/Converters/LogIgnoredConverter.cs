using System.Text.Json;
using System.Text.Json.Serialization;

namespace VidroApi.Application.Common.Logging.Converters;

public sealed class LogIgnoredConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
        => writer.WriteStringValue("log-ignored");
}
