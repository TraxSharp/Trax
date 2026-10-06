using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// Represents a manifest definition captured during startup configuration.
/// </summary>
/// <remarks>
/// PendingManifest uses a closure pattern to capture generic type information
/// at configuration time. This avoids reflection when processing manifests
/// during application startup and ensures type safety is preserved.
///
/// Pending manifests are collected during service configuration and seeded
/// automatically on startup by the ManifestPollingService.
/// </remarks>
internal class PendingManifest
{
    /// <summary>
    /// Gets or sets a closure that captures the generic type parameters and schedules the manifest.
    /// </summary>
    /// <remarks>
    /// This closure captures the TTrain and TInput generic types at configuration time,
    /// avoiding the need for reflection when the manifest is actually created during startup.
    /// The closure is invoked with an <see cref="ITraxScheduler"/> instance to perform
    /// the actual scheduling operation.
    /// </remarks>
    public required Func<
        ITraxScheduler,
        CancellationToken,
        Task<Manifest>
    > ScheduleFunc { get; init; }

    /// <summary>
    /// Gets or sets the external ID for logging and debugging purposes.
    /// </summary>
    /// <remarks>
    /// For single manifests, this is the actual ExternalId.
    /// For batch operations, this is a descriptive identifier like "sync-users... (batch)".
    /// </remarks>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Gets or sets the complete list of ExternalIds this PendingManifest will create or upsert.
    /// </summary>
    /// <remarks>
    /// Used by the startup reconciliation to determine which manifests in the database
    /// are still configured and which should be pruned. For single operations this contains
    /// just the one ExternalId; for batch operations it contains all ExternalIds computed
    /// from the source list and map function.
    /// </remarks>
    public required List<string> ExpectedExternalIds { get; init; }
}
