namespace VidroApi.Application.Common.Logging.Attributes;

/// <summary>Completely excludes this property from log output. Use for passwords, secrets, and tokens.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LogIgnoreAttribute : Attribute { }
