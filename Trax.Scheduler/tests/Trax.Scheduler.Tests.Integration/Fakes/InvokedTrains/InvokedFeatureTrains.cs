using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>The question <see cref="InvokedDecidingTrain"/> asks.</summary>
[Asks("Which lane does this invoked run take?")]
public enum InvokedLane
{
    Express,
    Ground,
}

public sealed record InvokedLaneTaken(string Lane, string Note);

public interface IInvokedDecidingTrain : IServiceTrain<InvokedStepInput, InvokedStepOutput>;

/// <summary>
/// Asks a decider which lane to take. The express lane fails, the ground lane succeeds with the artifact
/// <c>lane:ground:&lt;note&gt;</c>.
/// </summary>
public class InvokedDecidingTrain
    : ServiceTrain<InvokedStepInput, InvokedStepOutput>,
        IInvokedDecidingTrain
{
    protected override Task<Either<Exception, InvokedStepOutput>> Junctions() =>
        Switch<InvokedStepInput, InvokedLane>(tracks =>
                tracks
                    .When(InvokedLane.Express, t => t.Chain<InvokedTakeExpress>())
                    .When(InvokedLane.Ground, t => t.Chain<InvokedTakeGround>())
            )
            .Chain<InvokedLaneFinish>()
            .Resolve();
}

public sealed class InvokedTakeExpress : EffectJunction<InvokedStepInput, InvokedLaneTaken>
{
    public override Task<InvokedLaneTaken> Run(InvokedStepInput input) =>
        throw new InvalidOperationException("the express lane is closed");
}

public sealed class InvokedTakeGround : EffectJunction<InvokedStepInput, InvokedLaneTaken>
{
    public override Task<InvokedLaneTaken> Run(InvokedStepInput input) =>
        Task.FromResult(new InvokedLaneTaken("ground", input.Note));
}

public sealed class InvokedLaneFinish : EffectJunction<InvokedLaneTaken, InvokedStepOutput>
{
    public override Task<InvokedStepOutput> Run(InvokedLaneTaken input) =>
        Task.FromResult(
            new InvokedStepOutput { Artifact = $"lane:{input.Lane}:{input.Note}", Accepted = true }
        );
}

/// <summary>Answers each question with the next lane it was given, and counts what it was asked.</summary>
public sealed class InvokedLaneDecider : IDecider
{
    private readonly Queue<InvokedLane> _answers = new();
    private int _asked;

    public int Asked => _asked;

    public void Script(params InvokedLane[] answers)
    {
        lock (_answers)
        {
            _answers.Clear();
            foreach (var answer in answers)
                _answers.Enqueue(answer);
        }
        _asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);
        InvokedLane lane;
        lock (_answers)
            lane = _answers.Dequeue();
        return new ScriptedDecider().Choose(lane).Decide(request, cancellationToken);
    }
}

public interface IInvokedShortCircuitTrain : IServiceTrain<InvokedStepInput, InvokedStepOutput>;

/// <summary>
/// Short-circuits with the artifact <c>short:&lt;note&gt;</c> when its mode is <see cref="InvokedStepModes.Short"/>;
/// otherwise the short circuit fails, which is ignored, and the run ends as <see cref="InvokedStepTrain"/> does.
/// </summary>
public class InvokedShortCircuitTrain
    : ServiceTrain<InvokedStepInput, InvokedStepOutput>,
        IInvokedShortCircuitTrain
{
    protected override Task<Either<Exception, InvokedStepOutput>> Junctions() =>
        Chain<InvokedStepEnter>()
            .ShortCircuit<InvokedStepShortCut>()
            .Chain<InvokedStepFinish>()
            .Resolve();
}

public sealed class InvokedStepShortCut : EffectJunction<InvokedStepGated, InvokedStepOutput>
{
    public override Task<InvokedStepOutput> Run(InvokedStepGated input) =>
        input.Mode == InvokedStepModes.Short
            ? Task.FromResult(
                new InvokedStepOutput { Artifact = "short:" + input.Note, Accepted = true }
            )
            : throw new InvalidOperationException("no short cut");
}
