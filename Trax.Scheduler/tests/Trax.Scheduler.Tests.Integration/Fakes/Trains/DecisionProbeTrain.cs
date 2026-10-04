using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A train that asks a decider two questions, one per routing step, and records each track it
/// takes, so a test can tell a replayed run from one that asked afresh.
/// </summary>
/// <remarks>
/// Its chain can be made to fail before the first question, between the two, or after both
/// (<see cref="DecisionProbe.FailAt"/>), to leave a run that recorded none, one or both of its
/// decisions. Every host built over this test assembly registers it, so a host that starts with
/// chain verification on also needs an <see cref="IDecider"/>.
/// </remarks>
public class DecisionProbeTrain : ServiceTrain<DecisionProbeInput, string>, IDecisionProbeTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ProbeAdmit>()
            .Switch<DecisionProbeInput, ProbeLane>(tracks =>
                tracks
                    .When(ProbeLane.Fast, t => t.Chain<ProbeFastLane>())
                    .When(ProbeLane.Slow, t => t.Chain<ProbeSlowLane>())
            )
            .Chain<ProbeCheckpoint>()
            .Switch<DecisionProbeInput, ProbeSize>(tracks =>
                tracks
                    .When(ProbeSize.Small, t => t.Chain<ProbeSmall>())
                    .When(ProbeSize.Large, t => t.Chain<ProbeLarge>())
            )
            .Resolve();
}

public interface IDecisionProbeTrain : IServiceTrain<DecisionProbeInput, string>;

public record DecisionProbeInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}

[Asks("Which lane should this go down?")]
public enum ProbeLane
{
    Fast,
    Slow,
}

[Asks("How large is this?")]
public enum ProbeSize
{
    Small,
    Large,
}

/// <summary>Where <see cref="DecisionProbeTrain"/> fails, for a test that needs a partial run.</summary>
public enum ProbeFailure
{
    None,
    BeforeFirstQuestion,
    BetweenQuestions,

    /// <summary>After both questions are answered and both tracks taken.</summary>
    AfterQuestions,
}

/// <summary>What the probe train's runs did, shared because the scheduler builds the junctions.</summary>
public static class DecisionProbe
{
    /// <summary>Where the next run fails.</summary>
    public static ProbeFailure FailAt { get; set; }

    /// <summary>Each track taken, in order, as <c>(input value, track)</c>.</summary>
    public static ConcurrentQueue<(string Value, string Track)> Taken { get; } = new();

    public static void Reset()
    {
        FailAt = ProbeFailure.None;
        Taken.Clear();
    }

    public static IReadOnlyList<string> TracksOf(string value) =>
        Taken.Where(t => t.Value == value).Select(t => t.Track).ToList();
}

public record ProbeAdmitted(string Value);

public record ProbeLaneTaken(string Value);

public record ProbeChecked(string Value);

public class ProbeAdmit : Junction<DecisionProbeInput, ProbeAdmitted>
{
    public override Task<ProbeAdmitted> Run(DecisionProbeInput input) =>
        DecisionProbe.FailAt == ProbeFailure.BeforeFirstQuestion
            ? throw new InvalidOperationException("The probe failed before its first question.")
            : Task.FromResult(new ProbeAdmitted(input.Value));
}

public class ProbeFastLane : Junction<DecisionProbeInput, ProbeLaneTaken>
{
    public override Task<ProbeLaneTaken> Run(DecisionProbeInput input)
    {
        DecisionProbe.Taken.Enqueue((input.Value, nameof(ProbeLane.Fast)));
        return Task.FromResult(new ProbeLaneTaken(input.Value));
    }
}

public class ProbeSlowLane : Junction<DecisionProbeInput, ProbeLaneTaken>
{
    public override Task<ProbeLaneTaken> Run(DecisionProbeInput input)
    {
        DecisionProbe.Taken.Enqueue((input.Value, nameof(ProbeLane.Slow)));
        return Task.FromResult(new ProbeLaneTaken(input.Value));
    }
}

public class ProbeCheckpoint : Junction<ProbeLaneTaken, ProbeChecked>
{
    public override Task<ProbeChecked> Run(ProbeLaneTaken input) =>
        DecisionProbe.FailAt == ProbeFailure.BetweenQuestions
            ? throw new InvalidOperationException("The probe failed between its questions.")
            : Task.FromResult(new ProbeChecked(input.Value));
}

public class ProbeSmall : Junction<DecisionProbeInput, string>
{
    public override Task<string> Run(DecisionProbeInput input)
    {
        DecisionProbe.Taken.Enqueue((input.Value, nameof(ProbeSize.Small)));
        return DecisionProbe.FailAt == ProbeFailure.AfterQuestions
            ? throw new InvalidOperationException("The probe failed after its questions.")
            : Task.FromResult(nameof(ProbeSize.Small));
    }
}

public class ProbeLarge : Junction<DecisionProbeInput, string>
{
    public override Task<string> Run(DecisionProbeInput input)
    {
        DecisionProbe.Taken.Enqueue((input.Value, nameof(ProbeSize.Large)));
        return DecisionProbe.FailAt == ProbeFailure.AfterQuestions
            ? throw new InvalidOperationException("The probe failed after its questions.")
            : Task.FromResult(nameof(ProbeSize.Large));
    }
}
