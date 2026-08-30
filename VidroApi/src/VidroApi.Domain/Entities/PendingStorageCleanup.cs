using System.Diagnostics.CodeAnalysis;

namespace VidroApi.Domain.Entities;

public class PendingStorageCleanup : BaseEntity
{
    public const int ObjectPathMaxLength = 500;

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private PendingStorageCleanup() { }

    public PendingStorageCleanup(string objectPath, bool isPrefix, DateTimeOffset now) : base(now)
    {
        ObjectPath = objectPath;
        IsPrefix = isPrefix;
    }

    public string ObjectPath { get; init; } = null!;
    public bool IsPrefix { get; init; }
}
