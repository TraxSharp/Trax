using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// The Recovery sample's research chain with a checkpoint:
/// <c>PlanResearch → WriteResearchNote → Switch&lt;ResearchBrief, ResearchDepth&gt; →
/// FetchFullTexts → Checkpoint&lt;CheckedFindings&gt; → Switch&lt;CheckedFindings,
/// SummaryStyle&gt; → Summarize</c>. <see cref="ResearchProbe"/> records which junctions ran and
/// arms a crash.
/// </summary>
/// <remarks>
/// Every host built over this test assembly registers it, so a host that starts with chain
/// verification on also needs an <see cref="IDecider"/>, as <see cref="DecisionProbeTrain"/> does.
/// </remarks>
public class CheckpointResearchTrain : ServiceTrain<ResearchInput, string>, ICheckpointResearchTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Chain<WriteResearchNote>()
            .Switch<ResearchBrief, ResearchDepth>(tracks =>
                tracks
                    .When(ResearchDepth.Quick, t => t.Chain<SearchQuick>())
                    .When(ResearchDepth.Deep, t => t.Chain<SearchDeep>())
            )
            .Chain<FetchFullTexts>()
            .Checkpoint<CheckedFindings>()
            .Switch<CheckedFindings, SummaryStyle>(tracks =>
                tracks
                    .When(SummaryStyle.Short, t => t.Chain<SummarizeShort>())
                    .When(SummaryStyle.Long, t => t.Chain<SummarizeLong>())
            )
            .Resolve();
}

public interface ICheckpointResearchTrain : IServiceTrain<ResearchInput, string>;

public record ResearchInput : IManifestProperties
{
    public string Topic { get; set; } = string.Empty;
}

[Asks("How deep should the research go?")]
public enum ResearchDepth
{
    Quick,
    Deep,
}

[Asks("How long should the summary be?")]
public enum SummaryStyle
{
    Short,
    Long,
}

public sealed record ResearchBrief(string Topic);

public sealed record ResearchFindings(string Topic, string Depth);

public sealed record CheckedFindings(string Topic, string Depth, int Pages);

/// <summary>What the research train's runs did, shared because the scheduler builds the junctions.</summary>
public static class ResearchProbe
{
    /// <summary>The prefix of the manifest group the train writes before its checkpoint, one per topic.</summary>
    public const string NotePrefix = "research-note-";

    /// <summary>Holds the first junction to run, whichever it is, when given as <see cref="HoldIn"/>.</summary>
    public const string AnyJunction = "*";

    private static readonly TimeSpan HoldBound = TimeSpan.FromSeconds(30);

    private static TaskCompletionSource _released = NewGate();

    /// <summary>Each junction that ran, in order, as <c>(topic, junction)</c>.</summary>
    public static ConcurrentQueue<(string Topic, string Junction)> Ran { get; } = new();

    /// <summary>The junction that throws when it runs, or null.</summary>
    public static string? FailIn { get; set; }

    /// <summary>
    /// The junction that, once it starts, waits for <see cref="Release"/>; <see cref="AnyJunction"/>
    /// holds the first one to run. It holds once.
    /// </summary>
    public static string? HoldIn { get; set; }

    /// <summary>Released once when a junction is held.</summary>
    public static SemaphoreSlim Held { get; private set; } = new(0);

    public static IReadOnlyList<string> RanFor(string topic) =>
        Ran.Where(r => r.Topic == topic).Select(r => r.Junction).ToList();

    public static async Task Note(string topic, string junction)
    {
        Ran.Enqueue((topic, junction));

        if (HoldIn is { } hold && (hold == AnyJunction || hold == junction))
        {
            HoldIn = null;
            Held.Release();
            await _released.Task.WaitAsync(HoldBound);
        }

        if (FailIn == junction)
            throw new TimeoutException($"{junction} timed out");
    }

    /// <summary>Lets the held junction go on.</summary>
    public static void Release() => _released.TrySetResult();

    public static void Reset()
    {
        Ran.Clear();
        FailIn = null;
        HoldIn = null;
        _released.TrySetResult();
        _released = NewGate();
        Held = new SemaphoreSlim(0);
    }

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Answers the research train's questions with the choices set for them.</summary>
public sealed class ResearchDecider : IDecider
{
    private static readonly TimeSpan HoldBound = TimeSpan.FromSeconds(30);

    private int _asked;
    private bool _holdNext;
    private TaskCompletionSource _released = NewGate();

    public ResearchDepth Depth { get; set; } = ResearchDepth.Deep;

    public SummaryStyle Style { get; set; } = SummaryStyle.Long;

    public int Asked => _asked;

    /// <summary>Released once when a held question is asked: the run is between two junctions.</summary>
    public SemaphoreSlim Asking { get; private set; } = new(0);

    /// <summary>Makes the next question wait, once asked, until <see cref="Release"/>.</summary>
    public void HoldNextAsk() => _holdNext = true;

    /// <summary>Lets the held question be answered.</summary>
    public void Release() => _released.TrySetResult();

    public void Reset()
    {
        Depth = ResearchDepth.Deep;
        Style = SummaryStyle.Long;
        Interlocked.Exchange(ref _asked, 0);
        _holdNext = false;
        _released.TrySetResult();
        _released = NewGate();
        Asking = new SemaphoreSlim(0);
    }

    public async Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _asked);

        if (_holdNext)
        {
            _holdNext = false;
            Asking.Release();
            await _released.Task.WaitAsync(HoldBound, ct);
        }

        return new DecisionResult(
            new Dictionary<string, Answer>
            {
                [QuestionKey.For<ResearchDepth>()] = new ChoiceAnswer(Depth.ToString()),
                [QuestionKey.For<SummaryStyle>()] = new ChoiceAnswer(Style.ToString()),
            }
        );
    }

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class PlanResearch : EffectJunction<ResearchInput, ResearchBrief>
{
    public override async Task<ResearchBrief> Run(ResearchInput input)
    {
        await ResearchProbe.Note(input.Topic, nameof(PlanResearch));
        return new ResearchBrief(input.Topic);
    }
}

/// <summary>A write committed before the checkpoint: one manifest group per topic.</summary>
public sealed class WriteResearchNote(IDataContext context)
    : EffectJunction<ResearchBrief, ResearchBrief>
{
    public override async Task<ResearchBrief> Run(ResearchBrief input)
    {
        await ResearchProbe.Note(input.Topic, nameof(WriteResearchNote));
        await context.Track(
            new ManifestGroup
            {
                Name = ResearchProbe.NotePrefix + input.Topic + "-" + Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        );
        await context.SaveChanges(CancellationToken.None);
        return input;
    }
}

public sealed class SearchQuick : EffectJunction<ResearchBrief, ResearchFindings>
{
    public override async Task<ResearchFindings> Run(ResearchBrief input)
    {
        await ResearchProbe.Note(input.Topic, nameof(SearchQuick));
        return new ResearchFindings(input.Topic, "quick");
    }
}

public sealed class SearchDeep : EffectJunction<ResearchBrief, ResearchFindings>
{
    public override async Task<ResearchFindings> Run(ResearchBrief input)
    {
        await ResearchProbe.Note(input.Topic, nameof(SearchDeep));
        return new ResearchFindings(input.Topic, "deep");
    }
}

public sealed class FetchFullTexts : EffectJunction<ResearchFindings, CheckedFindings>
{
    public override async Task<CheckedFindings> Run(ResearchFindings input)
    {
        await ResearchProbe.Note(input.Topic, nameof(FetchFullTexts));
        return new CheckedFindings(input.Topic, input.Depth, 12);
    }
}

public sealed class SummarizeShort : EffectJunction<CheckedFindings, string>
{
    public override async Task<string> Run(CheckedFindings input)
    {
        await ResearchProbe.Note(input.Topic, nameof(SummarizeShort));
        return $"short {input.Depth} summary of {input.Topic}";
    }
}

public sealed class SummarizeLong : EffectJunction<CheckedFindings, string>
{
    public override async Task<string> Run(CheckedFindings input)
    {
        await ResearchProbe.Note(input.Topic, nameof(SummarizeLong));
        return $"long {input.Depth} summary of {input.Topic}, {input.Pages} pages";
    }
}
