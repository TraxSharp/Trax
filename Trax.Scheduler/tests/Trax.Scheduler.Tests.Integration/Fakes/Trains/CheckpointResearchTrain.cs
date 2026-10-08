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

    /// <summary>Each junction that ran, in order, as <c>(topic, junction)</c>.</summary>
    public static ConcurrentQueue<(string Topic, string Junction)> Ran { get; } = new();

    /// <summary>The junction that throws when it runs, or null.</summary>
    public static string? FailIn { get; set; }

    public static IReadOnlyList<string> RanFor(string topic) =>
        Ran.Where(r => r.Topic == topic).Select(r => r.Junction).ToList();

    public static void Note(string topic, string junction)
    {
        Ran.Enqueue((topic, junction));

        if (FailIn == junction)
            throw new TimeoutException($"{junction} timed out");
    }

    public static void Reset()
    {
        Ran.Clear();
        FailIn = null;
    }
}

/// <summary>Answers the research train's questions with the choices set for them.</summary>
public sealed class ResearchDecider : IDecider
{
    private int _asked;

    public ResearchDepth Depth { get; set; } = ResearchDepth.Deep;

    public SummaryStyle Style { get; set; } = SummaryStyle.Long;

    public int Asked => _asked;

    public void Reset()
    {
        Depth = ResearchDepth.Deep;
        Style = SummaryStyle.Long;
        Interlocked.Exchange(ref _asked, 0);
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _asked);
        return Task.FromResult(
            new DecisionResult(
                new Dictionary<string, Answer>
                {
                    [QuestionKey.For<ResearchDepth>()] = new ChoiceAnswer(Depth.ToString()),
                    [QuestionKey.For<SummaryStyle>()] = new ChoiceAnswer(Style.ToString()),
                }
            )
        );
    }
}

public sealed class PlanResearch : EffectJunction<ResearchInput, ResearchBrief>
{
    public override Task<ResearchBrief> Run(ResearchInput input)
    {
        ResearchProbe.Note(input.Topic, nameof(PlanResearch));
        return Task.FromResult(new ResearchBrief(input.Topic));
    }
}

/// <summary>A write committed before the checkpoint: one manifest group per topic.</summary>
public sealed class WriteResearchNote(IDataContext context)
    : EffectJunction<ResearchBrief, ResearchBrief>
{
    public override async Task<ResearchBrief> Run(ResearchBrief input)
    {
        ResearchProbe.Note(input.Topic, nameof(WriteResearchNote));
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
    public override Task<ResearchFindings> Run(ResearchBrief input)
    {
        ResearchProbe.Note(input.Topic, nameof(SearchQuick));
        return Task.FromResult(new ResearchFindings(input.Topic, "quick"));
    }
}

public sealed class SearchDeep : EffectJunction<ResearchBrief, ResearchFindings>
{
    public override Task<ResearchFindings> Run(ResearchBrief input)
    {
        ResearchProbe.Note(input.Topic, nameof(SearchDeep));
        return Task.FromResult(new ResearchFindings(input.Topic, "deep"));
    }
}

public sealed class FetchFullTexts : EffectJunction<ResearchFindings, CheckedFindings>
{
    public override Task<CheckedFindings> Run(ResearchFindings input)
    {
        ResearchProbe.Note(input.Topic, nameof(FetchFullTexts));
        return Task.FromResult(new CheckedFindings(input.Topic, input.Depth, 12));
    }
}

public sealed class SummarizeShort : EffectJunction<CheckedFindings, string>
{
    public override Task<string> Run(CheckedFindings input)
    {
        ResearchProbe.Note(input.Topic, nameof(SummarizeShort));
        return Task.FromResult($"short {input.Depth} summary of {input.Topic}");
    }
}

public sealed class SummarizeLong : EffectJunction<CheckedFindings, string>
{
    public override Task<string> Run(CheckedFindings input)
    {
        ResearchProbe.Note(input.Topic, nameof(SummarizeLong));
        return Task.FromResult($"long {input.Depth} summary of {input.Topic}, {input.Pages} pages");
    }
}
