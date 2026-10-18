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

public interface IParallelRemoteTrain : IServiceTrain<ParallelRemoteInput, string>;

public record ParallelRemoteInput : IManifestProperties
{
    public string Topic { get; set; } = string.Empty;
}

public sealed record ParallelRemoteLeft(int Score);

public sealed record ParallelRemoteRefinedLeft(int Score);

public sealed record ParallelRemoteRight(int Score);

public class ParallelRemoteAdmit : EffectJunction<ParallelRemoteInput, ParallelRemoteInput>
{
    public override Task<ParallelRemoteInput> Run(ParallelRemoteInput input) =>
        Task.FromResult(input);
}

public class ParallelRemoteScoreLeft : EffectJunction<ParallelRemoteInput, ParallelRemoteLeft>
{
    public override Task<ParallelRemoteLeft> Run(ParallelRemoteInput input) =>
        Task.FromResult(new ParallelRemoteLeft(input.Topic.Length));
}

public class ParallelRemoteRefineLeft
    : EffectJunction<ParallelRemoteLeft, ParallelRemoteRefinedLeft>
{
    public override Task<ParallelRemoteRefinedLeft> Run(ParallelRemoteLeft input) =>
        Task.FromResult(new ParallelRemoteRefinedLeft(input.Score * 2));
}

public class ParallelRemoteScoreRight : EffectJunction<ParallelRemoteInput, ParallelRemoteRight>
{
    public override Task<ParallelRemoteRight> Run(ParallelRemoteInput input) =>
        Task.FromResult(new ParallelRemoteRight(input.Topic.Length * 10));
}

public class ParallelRemoteCombine
    : EffectJunction<(ParallelRemoteRefinedLeft, ParallelRemoteRight), string>
{
    public override Task<string> Run((ParallelRemoteRefinedLeft, ParallelRemoteRight) input) =>
        Task.FromResult($"left={input.Item1.Score}, right={input.Item2.Score}");
}
