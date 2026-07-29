namespace Trax.Effect.Services.ChangeSignal;

/// <summary>
/// Identifies which slice of scheduler/admin data changed. Carried by
/// <see cref="ITraxChangeSignal"/> so a subscriber can refetch just the affected
/// view instead of polling. The signal names the domain only, never row data.
/// </summary>
public enum ChangeDomain
{
    /// <summary>The work queue (entries queued, dispatched, or cancelled).</summary>
    WorkQueue,

    /// <summary>Dead letters (created when retries are exhausted, or requeued/acknowledged).</summary>
    DeadLetter,

    /// <summary>Manifest scheduling/retry settings (edited, enabled, or disabled).</summary>
    Manifest,

    /// <summary>Manifest group configuration.</summary>
    ManifestGroup,

    /// <summary>Scheduler configuration.</summary>
    SchedulerConfig,
}
