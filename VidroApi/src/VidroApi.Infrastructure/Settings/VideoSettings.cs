namespace VidroApi.Infrastructure.Settings;

public class VideoSettings
{
    public int MaxTagsPerVideo { get; set; }
    public int ReconciliationIntervalMinutes { get; set; }
    public int ViewDeduplicationWindowHours { get; set; }

    /// <summary>
    /// How long a video may sit in Processing before reconciliation gives up on it.
    /// Must stay above the Processor's own job budget + orphan-requeue threshold
    /// (18min + 19min at PROCESSING_TIMEOUT_SCALE=1), otherwise a healthy job that the
    /// Processor is still retrying gets marked Failed here.
    /// </summary>
    public int ProcessingTimeoutMinutes { get; set; } = 45;
}
