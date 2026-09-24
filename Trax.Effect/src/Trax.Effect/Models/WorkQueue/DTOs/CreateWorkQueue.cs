namespace Trax.Effect.Models.WorkQueue.DTOs;

/// <summary>
/// Data transfer object for creating a new WorkQueue entry.
/// </summary>
public class CreateWorkQueue
{
    /// <summary>
    /// The fully qualified train type name to execute.
    /// </summary>
    public required string TrainName { get; set; }

    /// <summary>
    /// Serialized train input (JSON). Same format as Manifest.Properties.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Fully qualified type name of the input, for deserialization.
    /// </summary>
    public string? InputTypeName { get; set; }

    /// <summary>
    /// Optional manifest ID when this entry was queued from a scheduled manifest.
    /// </summary>
    public long? ManifestId { get; set; }

    /// <summary>
    /// Dispatch priority. Range: 0-31 (clamped in WorkQueue.Create). Defaults to 0.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// The earliest time this work queue entry should be dispatched.
    /// Null means dispatch immediately.
    /// </summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>
    /// Optional dead letter ID when this entry is created by requeuing a dead letter.
    /// </summary>
    public long? DeadLetterId { get; set; }

    /// <summary>
    /// Identifies what this work touches. Entries sharing a non-null key are not dispatched
    /// concurrently. Null means no serialization. <see cref="WorkQueue.Create"/> refuses an empty
    /// key, and one longer than <see cref="WorkQueue.MaxSubjectKeyLength"/>.
    /// </summary>
    public string? SubjectKey { get; set; }

    /// <summary>
    /// Leaves the entry unconfirmed, and therefore undispatchable, until something promotes it.
    /// Defaults to false, so an entry is dispatchable as soon as it is committed.
    /// </summary>
    /// <remarks>
    /// Set this when a side-effect must be durable before the work may run — the enqueue then
    /// commits the entry, performs the side-effect, and promotes the entry in a second commit, so
    /// a crash in between strands a detectable row rather than an invisible side-effect.
    /// </remarks>
    public bool DeferPromotion { get; set; }
}
