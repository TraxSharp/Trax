namespace Trax.Effect.Models.DeadLetter.DTOs;

/// <summary>
/// The values <see cref="DeadLetter.Create"/> needs to dead-letter a manifest. Infrastructure used
/// by the scheduler's manifest manager when a manifest's failed runs reach its <c>MaxRetries</c>;
/// not intended to be used directly.
/// </summary>
public class CreateDeadLetter
{
    /// <summary>
    /// The manifest whose runs failed. Its <c>Id</c> becomes the dead letter's
    /// <c>ManifestId</c>, so it must be a manifest already saved to the database.
    /// </summary>
    public required Manifest.Manifest Manifest { get; set; }

    /// <summary>
    /// Human-readable reason stored in the <c>reason</c> column, for example
    /// <c>"Max retries exceeded: (3) failures &gt;= (3) max retries"</c>.
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// How many failed runs the manifest had when it was dead-lettered. Stored as
    /// <c>RetryCountAtDeadLetter</c>.
    /// </summary>
    public required int RetryCount { get; set; }
}
