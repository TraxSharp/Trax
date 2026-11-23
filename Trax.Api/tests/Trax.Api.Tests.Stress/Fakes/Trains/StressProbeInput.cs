using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Stress.Fakes.Trains;

public record StressProbeInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}
