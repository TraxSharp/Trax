using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public record ParallelRemoteInput : IManifestProperties
{
    public string Topic { get; set; } = string.Empty;
}
