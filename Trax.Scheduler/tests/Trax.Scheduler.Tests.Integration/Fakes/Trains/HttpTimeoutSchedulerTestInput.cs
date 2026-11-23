using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Input for <see cref="HttpTimeoutSchedulerTestTrain"/>.</summary>
public record HttpTimeoutSchedulerTestInput : IManifestProperties
{
    public int TimeoutMilliseconds { get; set; } = 100;
}
