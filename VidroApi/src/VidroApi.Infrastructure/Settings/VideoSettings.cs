namespace VidroApi.Infrastructure.Settings;

public class VideoSettings
{
    public int MaxTagsPerVideo { get; set; }
    public int ReconciliationIntervalMinutes { get; set; }
    public int ViewDeduplicationWindowHours { get; set; }

    /// <summary>
    /// How long a video may sit in Processing before reconciliation gives up on it. Must cover
    /// the Processor's worst case with every retry spent, otherwise a job it is still retrying
    /// gets marked Failed here and its late success webhook is dropped. At
    /// PROCESSING_TIMEOUT_SCALE=1 that worst case is 4 attempts (<c>MaxJobRetries</c> 3 + 1,
    /// <c>VidroProcessor/queue/job.go</c>) × 20 min (an orphaned attempt: <c>OrphanThreshold</c> =
    /// <c>JobBudget</c> 18 min + 1 min, <c>internal/worker/worker.go</c>, plus the 1 min
    /// <c>RecoveryInterval</c>, <c>queue/client.go</c>) = 80 min, pinned in
    /// <c>contracts/processing-timeout.json</c>. 90 leaves 10 min for webhook retries and queue
    /// wait. Raising PROCESSING_TIMEOUT_SCALE on the worker means scaling this too.
    /// </summary>
    // ponytail: queue wait is unbounded — a backlog longer than the 10 min slack still trips this.
    public int ProcessingTimeoutMinutes { get; set; } = 90;
}
