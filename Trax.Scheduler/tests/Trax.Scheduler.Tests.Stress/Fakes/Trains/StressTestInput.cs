using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Stress.Fakes.Trains;

public record StressTestInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}
