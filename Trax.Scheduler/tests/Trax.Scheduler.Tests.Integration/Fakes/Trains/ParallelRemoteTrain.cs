using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A scheduled train with a <c>Parallel</c> step: a junction before it, a <c>left</c> branch of
/// two junctions and a <c>right</c> branch of one, and a junction after the join that reads both
/// branches' results.
/// </summary>
public class ParallelRemoteTrain : ServiceTrain<ParallelRemoteInput, string>, IParallelRemoteTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ParallelRemoteAdmit>()
            .Parallel(p =>
                p.Branch(
                        "left",
                        b => b.Chain<ParallelRemoteScoreLeft>().Chain<ParallelRemoteRefineLeft>()
                    )
                    .Branch("right", b => b.Chain<ParallelRemoteScoreRight>())
            )
            .Chain<ParallelRemoteCombine>()
            .Resolve();
}
