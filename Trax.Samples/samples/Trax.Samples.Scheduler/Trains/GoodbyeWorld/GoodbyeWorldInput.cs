using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Trains.GoodbyeWorld;

/// <summary>
/// Input for the GoodbyeWorld train.
/// Implements IManifestProperties to enable serialization for scheduled jobs.
/// </summary>
public record GoodbyeWorldInput : IManifestProperties
{
    /// <summary>
    /// The name to say goodbye to in the train.
    /// </summary>
    public string Name { get; init; } = "World";
}
