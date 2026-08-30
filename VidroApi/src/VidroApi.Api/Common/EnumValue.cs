namespace VidroApi.Api.Common;

public record EnumValue
{
    public int Id { get; init; }
    public string Value { get; init; } = null!;

    public static EnumValue From<T>(T value) where T : Enum =>
        new()
        {
            Id = Convert.ToInt32(value),
            Value = value.ToString()
        };
}
