namespace VidroApi.Application.Common.Logging.Attributes;

/// <summary>Masks this string property in log output, optionally keeping the first/last N characters.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LogMaskAttribute(int keepFirst = 0, int keepLast = 0, char maskChar = '*') : Attribute
{
    public int KeepFirst { get; } = keepFirst;
    public int KeepLast  { get; } = keepLast;
    public char MaskChar { get; } = maskChar;
}
