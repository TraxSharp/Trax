using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// Input for the typed output scheduler test train.
/// </summary>
public record TypedOutputSchedulerTestInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}
