using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Two branches side by side, the second holding a <c>Parallel</c> of its own, joined by one step.
/// Its first junction waits for the test, as the other marker trains' does.
/// </summary>
[TraxAuthorize(Roles = "Admin")]
public class ParallelMarkerTrain : ServiceTrain<MarkerInput, string>, IParallelMarkerTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<HoldForRelease>()
            .Parallel(p =>
                p.Branch("left", b => b.Chain<ScoreLeft>())
                    .Branch(
                        "right",
                        b =>
                            b.Chain<ScoreRight>()
                                .Parallel(q =>
                                    q.Branch("x", c => c.Chain<CountX>())
                                        .Branch("y", c => c.Chain<CountY>())
                                )
                    )
            )
            .Chain<JoinScores>()
            .Resolve();
}
