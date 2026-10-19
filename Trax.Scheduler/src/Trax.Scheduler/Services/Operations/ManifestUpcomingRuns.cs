namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// When one manifest runs next, and its schedule in plain words. Returned by
/// <see cref="IOperationsService.GetUpcomingRunsAsync"/>.
/// </summary>
/// <param name="ManifestId">The manifest.</param>
/// <param name="ScheduleText">
/// The schedule in plain words: "Daily at 02:00 UTC", "Every 5 minutes", "After nightly-import
/// succeeds", "On demand". A cron expression with no plain reading is shown as itself, in UTC.
/// </param>
/// <param name="NextRuns">
/// The next times the manifest manager will find it due, earliest first, in UTC; the first can be
/// now when it is due already. Empty for a disabled manifest, one in a disabled group, one with a
/// dead letter awaiting intervention, and every schedule type with no time (manual, on demand,
/// dependent and dormant dependent).
/// </param>
/// <param name="RunsAfterManifestId">
/// For a dependent manifest that can run, the manifest whose success starts it; otherwise null.
/// </param>
/// <param name="RunsAfterManifestExternalId">That manifest's external id, or null.</param>
public sealed record ManifestUpcomingRuns(
    long ManifestId,
    string ScheduleText,
    IReadOnlyList<DateTime> NextRuns,
    long? RunsAfterManifestId,
    string? RunsAfterManifestExternalId
);
