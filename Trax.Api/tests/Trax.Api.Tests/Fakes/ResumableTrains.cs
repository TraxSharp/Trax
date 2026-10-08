using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

/// <summary>
/// A research chain with a checkpoint, for the operator's resume:
/// <c>AddServices(decider) → GatherSources → Switch&lt;Sources, ResumeVault&gt; → FetchSources →
/// Checkpoint&lt;FetchedSources&gt; → SummarizeSources → PublishSummary</c>. The vault is a
/// sensitive question, so the track taken is withheld from every record, and the checkpoint's
/// state carries <see cref="ResumeProbe.StateSecret"/>, which the input does not, so a surface
/// that showed the stored state would show it. <see cref="ResumeProbe"/> arms a failure.
/// </summary>
/// <remarks>
/// The decider is carried by the chain rather than registered, so the other hosts that scan this
/// assembly need none.
/// </remarks>
public abstract class ResumableTrainBase : ServiceTrain<ResumableInput, string>
{
    private static readonly IDecider Decider = new ScriptedDecider().Choose(
        ResumeVault.WestWing,
        0.9
    );

    protected override Task<Either<Exception, string>> Junctions() =>
        AddServices(Decider)
            .Chain<GatherSources>()
            .Switch<Sources, ResumeVault>(tracks =>
                tracks
                    .When(ResumeVault.EastWing, t => t.Chain<OpenEastWing>())
                    .When(ResumeVault.WestWing, t => t.Chain<OpenWestWing>())
            )
            .Chain<FetchSources>()
            .Checkpoint<FetchedSources>()
            .Chain<SummarizeSources>()
            .Chain<PublishSummary>()
            .Resolve();
}

public interface IResumableTrain : IServiceTrain<ResumableInput, string>;

/// <summary>Resumable by anyone past the operations gate: it declares no requirement of its own.</summary>
public class ResumableTrain : ResumableTrainBase, IResumableTrain;

public interface IGuardedResumableTrain : IServiceTrain<ResumableInput, string>;

/// <summary>Queued, and so resumed, only by a caller in the <c>Resumer</c> role.</summary>
[TraxAuthorize(Roles = "Resumer")]
public class GuardedResumableTrain : ResumableTrainBase, IGuardedResumableTrain;

public record ResumableInput
{
    public string Topic { get; init; } = "";
}

/// <summary>Which wing holds the documents: an answer withheld from every record.</summary>
[TraxSensitive]
[Asks("Which wing of the vault holds the documents?")]
public enum ResumeVault
{
    EastWing,
    WestWing,
}

public sealed record Sources(string Topic);

public sealed record FetchedSources(string Topic, int Pages, string Digest);

public sealed record SourceSummary(string Text);

/// <summary>What the resumable trains ran, and where the next run fails.</summary>
public static class ResumeProbe
{
    /// <summary>Held only in the checkpoint's state: never in an input, an output or a message.</summary>
    public const string StateSecret = "CHECKPOINT-STATE-5e1d9a";

    private static readonly Lock Gate = new();
    private static readonly List<string> RanList = [];

    /// <summary>The junction that throws when it runs, or null.</summary>
    public static string? FailIn { get; set; }

    public static IReadOnlyList<string> Ran
    {
        get
        {
            lock (Gate)
                return [.. RanList];
        }
    }

    public static void Note(string junction)
    {
        lock (Gate)
            RanList.Add(junction);

        if (FailIn == junction)
            throw new TimeoutException($"{junction} timed out");
    }

    public static void Reset()
    {
        lock (Gate)
            RanList.Clear();
        FailIn = null;
    }
}

public sealed class GatherSources : EffectJunction<ResumableInput, Sources>
{
    public override Task<Sources> Run(ResumableInput input)
    {
        ResumeProbe.Note(nameof(GatherSources));
        return Task.FromResult(new Sources(input.Topic));
    }
}

public sealed class OpenEastWing : EffectJunction<Sources, Sources>
{
    public override Task<Sources> Run(Sources input)
    {
        ResumeProbe.Note(nameof(OpenEastWing));
        return Task.FromResult(input);
    }
}

public sealed class OpenWestWing : EffectJunction<Sources, Sources>
{
    public override Task<Sources> Run(Sources input)
    {
        ResumeProbe.Note(nameof(OpenWestWing));
        return Task.FromResult(input);
    }
}

public sealed class FetchSources : EffectJunction<Sources, FetchedSources>
{
    public override Task<FetchedSources> Run(Sources input)
    {
        ResumeProbe.Note(nameof(FetchSources));
        return Task.FromResult(new FetchedSources(input.Topic, 12, ResumeProbe.StateSecret));
    }
}

public sealed class SummarizeSources : EffectJunction<FetchedSources, SourceSummary>
{
    public override Task<SourceSummary> Run(FetchedSources input)
    {
        ResumeProbe.Note(nameof(SummarizeSources));
        return Task.FromResult(new SourceSummary($"{input.Pages} pages on {input.Topic}"));
    }
}

public sealed class PublishSummary : EffectJunction<SourceSummary, string>
{
    public override Task<string> Run(SourceSummary input)
    {
        ResumeProbe.Note(nameof(PublishSummary));
        return Task.FromResult(input.Text);
    }
}
