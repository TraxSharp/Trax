using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Trains.AlwaysFails;

/// <summary>
/// Input for the AlwaysFails train.
/// This train is intentionally designed to always throw an exception,
/// generating dead letters for testing the dead letter detail page.
/// </summary>
public record AlwaysFailsInput : IManifestProperties
{
    /// <summary>
    /// A label identifying this failure scenario.
    /// </summary>
    public string Scenario { get; init; } = "Simulated Failure";
}
