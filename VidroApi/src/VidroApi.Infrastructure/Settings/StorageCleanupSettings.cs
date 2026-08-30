using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class StorageCleanupSettings
{
    [Range(1, int.MaxValue)]
    public int IntervalMinutes { get; set; }

    [Range(1, int.MaxValue)]
    public int BatchSize { get; set; }
}
