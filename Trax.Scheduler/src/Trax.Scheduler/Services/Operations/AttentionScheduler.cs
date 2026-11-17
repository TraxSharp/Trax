namespace Trax.Scheduler.Services.Operations;

/// <summary>Whether the scheduler's two loops are switched on, as the live settings hold them.</summary>
/// <param name="ManifestManagerEnabled">Whether the manifest manager queues scheduled work.</param>
/// <param name="JobDispatcherEnabled">Whether the job dispatcher moves queued work to workers.</param>
/// <param name="Link">The console's scheduler settings, where both are switched.</param>
public sealed record AttentionScheduler(
    bool ManifestManagerEnabled,
    bool JobDispatcherEnabled,
    string Link
)
{
    /// <summary>True when either loop is switched off.</summary>
    public bool Paused => !ManifestManagerEnabled || !JobDispatcherEnabled;
}
