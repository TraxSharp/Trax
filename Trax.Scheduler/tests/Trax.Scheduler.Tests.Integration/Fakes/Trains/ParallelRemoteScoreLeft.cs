using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ParallelRemoteScoreLeft : EffectJunction<ParallelRemoteInput, ParallelRemoteLeft>
{
    public override Task<ParallelRemoteLeft> Run(ParallelRemoteInput input) =>
        Task.FromResult(new ParallelRemoteLeft(input.Topic.Length));
}
