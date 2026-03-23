namespace Trax.Effect.Models.BackgroundJob.DTOs;

/// <summary>
/// Data transfer object for creating a new BackgroundJob entry.
/// </summary>
public class CreateBackgroundJob
{
    /// <summary>
    /// The ID of the Metadata record representing this job execution.
    /// </summary>
    public required long MetadataId { get; set; }

    /// <summary>
    /// Serialized train input (JSON), for ad-hoc executions.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Fully qualified type name of the input, for deserialization.
    /// </summary>
    public string? InputType { get; set; }

    /// <summary>
    /// Job priority. Higher values are dequeued first. Defaults to 0.
    /// </summary>
    public int Priority { get; set; }
}
