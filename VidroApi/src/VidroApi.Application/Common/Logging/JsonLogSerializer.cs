using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using VidroApi.Application.Common.Logging.Attributes;
using VidroApi.Application.Common.Logging.Converters;

namespace VidroApi.Application.Common.Logging;

public static class JsonLogSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ApplyLogAttributes } },
        WriteIndented = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void ApplyLogAttributes(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var prop in typeInfo.Properties)
        {
            var converter = ResolveConverter(prop);
            if (converter is not null)
                prop.CustomConverter = converter;
        }
    }

    private static JsonConverter? ResolveConverter(JsonPropertyInfo prop)
    {
        var provider = prop.AttributeProvider;
        if (provider is null)
            return null;

        if (HasAttribute<LogIgnoreAttribute>(provider))
            return new LogIgnoredConverter();

        if (prop.PropertyType == typeof(string) &&
            provider.GetCustomAttributes(typeof(LogMaskAttribute), inherit: true) is { Length: > 0 } attrs)
        {
            var mask = (LogMaskAttribute)attrs[0];
            return new MaskingStringConverter(mask.KeepFirst, mask.KeepLast, mask.MaskChar);
        }

        return null;
    }

    private static bool HasAttribute<TAttr>(System.Reflection.ICustomAttributeProvider provider) where TAttr : Attribute
        => provider.GetCustomAttributes(typeof(TAttr), inherit: true)?.Length > 0;

    /// <summary>
    /// Wraps a pre-serialized JSON string so Serilog logs it as raw JSON
    /// instead of re-serializing it as an escaped string.
    /// </summary>
    public readonly struct RawJson(string? json)
    {
        public override string ToString() => json ?? "null";
        public static implicit operator RawJson(string? json) => new(json);
    }
}
