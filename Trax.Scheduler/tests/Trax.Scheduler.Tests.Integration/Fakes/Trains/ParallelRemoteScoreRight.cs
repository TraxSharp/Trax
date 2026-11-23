using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ParallelRemoteScoreRight : EffectJunction<ParallelRemoteInput, ParallelRemoteRight>
{
    public override Task<ParallelRemoteRight> Run(ParallelRemoteInput input) =>
        Task.FromResult(new ParallelRemoteRight(input.Topic.Length * 10));
}
