using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

public sealed record LeftScore(int Value);

public sealed record RightScore(int Value);

public sealed record XCount(int Value);

public sealed record YCount(int Value);

/// <summary>Fails when the run is asked to, so its branch fails the <c>Parallel</c> step.</summary>
public class ScoreLeft : EffectJunction<MarkerInput, LeftScore>
{
    public override Task<LeftScore> Run(MarkerInput input) =>
        input.Fail
            ? throw new InvalidOperationException("the left branch failed")
            : Task.FromResult(new LeftScore(1));
}

public class ScoreRight : EffectJunction<MarkerInput, RightScore>
{
    public override Task<RightScore> Run(MarkerInput input) => Task.FromResult(new RightScore(2));
}

public class CountX : EffectJunction<MarkerInput, XCount>
{
    public override Task<XCount> Run(MarkerInput input) => Task.FromResult(new XCount(3));
}

public class CountY : EffectJunction<MarkerInput, YCount>
{
    public override Task<YCount> Run(MarkerInput input) => Task.FromResult(new YCount(4));
}

public class JoinScores : EffectJunction<(LeftScore, RightScore), string>
{
    public override Task<string> Run((LeftScore, RightScore) input) =>
        Task.FromResult($"{input.Item1.Value}+{input.Item2.Value}");
}

public interface IParallelMarkerTrain : IServiceTrain<MarkerInput, string>;

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
