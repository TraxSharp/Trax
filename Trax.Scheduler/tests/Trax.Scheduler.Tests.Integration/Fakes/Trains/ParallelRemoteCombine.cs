using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ParallelRemoteCombine
    : EffectJunction<(ParallelRemoteRefinedLeft, ParallelRemoteRight), string>
{
    public override Task<string> Run((ParallelRemoteRefinedLeft, ParallelRemoteRight) input) =>
        Task.FromResult(
            string.Create(
                CultureInfo.InvariantCulture,
                $"left={input.Item1.Score}, right={input.Item2.Score}"
            )
        );
}
