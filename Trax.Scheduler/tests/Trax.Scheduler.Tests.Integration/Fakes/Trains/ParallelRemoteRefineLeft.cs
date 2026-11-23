using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ParallelRemoteRefineLeft
    : EffectJunction<ParallelRemoteLeft, ParallelRemoteRefinedLeft>
{
    public override Task<ParallelRemoteRefinedLeft> Run(ParallelRemoteLeft input) =>
        Task.FromResult(new ParallelRemoteRefinedLeft(input.Score * 2));
}
