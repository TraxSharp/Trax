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
}
