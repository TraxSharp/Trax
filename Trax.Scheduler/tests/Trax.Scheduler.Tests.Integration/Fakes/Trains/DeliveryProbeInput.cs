using System.Collections.Concurrent;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Input for <see cref="DeliveryProbeTrain"/>.</summary>
public record DeliveryProbeInput : IManifestProperties
{
    public string Key { get; set; } = string.Empty;

    /// <summary>When true, the body throws after counting itself.</summary>
    public bool Fail { get; set; }
}
