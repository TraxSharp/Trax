using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

/// <summary>
/// Trains for the checkpoint and resume tests, shaped like the Recovery sample's research chain:
/// plan, route to a source, fetch, checkpoint, score, checkpoint, summarise. <see cref="Probe"/>
/// records which junctions ran and arms the failures.
/// </summary>
public static class CheckpointProbe
{
    private static readonly object Gate = new();

    public static List<string> Ran { get; } = [];

    /// <summary>The junction that throws when it runs, or null.</summary>
    public static string? FailIn { get; set; }

    /// <summary>How <see cref="MakeDirty"/> leaves the step's data context.</summary>
    public static DirtyMode Dirty { get; set; }

    /// <summary>The pages <see cref="FetchFullTexts"/> reports; a large value makes a large state.</summary>
    public static string Padding { get; set; } = "";

    /// <summary>Released once both branches of <see cref="TwoBranchTrain"/> reach their checkpoint.</summary>
    public static CountdownEvent? BothBranches { get; set; }

    /// <summary>Reads how many saves the effect providers have made, when a test counts them.</summary>
    public static Func<int>? SaveCount { get; set; }

    /// <summary>The save count each junction saw when it ran.</summary>
    public static Dictionary<string, int> SavesAt { get; } = [];

    public static void Note(string junction)
    {
        lock (Gate)
        {
            Ran.Add(junction);
            if (SaveCount is { } count)
                SavesAt[junction] = count();
        }

        if (FailIn == junction)
            throw new TimeoutException($"{junction} timed out");
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Ran.Clear();
            SavesAt.Clear();
        }
        SaveCount = null;
        FailIn = null;
        Dirty = DirtyMode.Clean;
        Padding = "";
        BothBranches = null;
    }
}

public enum DirtyMode
{
    Clean,
    UnsavedChange,
    OpenTransaction,
}

[Asks("Which source should the research use?")]
public enum ResearchSource
{
    Web,
    Papers,
}

/// <summary>A routing question whose answer is withheld from every record.</summary>
[TraxSensitive]
[Asks("Which vault holds the documents?")]
public enum Vault
{
    Left,
    Right,
}

public sealed record Brief(string Topic);

public sealed record Findings(string Topic, string Source);

public sealed record CheckedFindings(string Topic, string Source, int Pages, string Padding);

public sealed record Scored(string Topic, string Source, int Score);

/// <summary>A state that reaches a sensitive member.</summary>
public sealed record SecretFindings(string Topic, [property: TraxSensitive] string Token);

public sealed record BranchA(string Topic);

public sealed record BranchB(string Topic);

/// <summary>Answers every question with the choice set for it.</summary>
public sealed class ScriptedResearchDecider : IDecider
{
    public Dictionary<string, string> Choices { get; } = [];

    public int Asked { get; set; }

    public void Reset()
    {
        Choices.Clear();
        Choices[QuestionKey.For<ResearchSource>()] = nameof(ResearchSource.Papers);
        Choices[QuestionKey.For<Vault>()] = nameof(Vault.Left);
        Asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
    {
        Asked++;
        return Task.FromResult(
            new DecisionResult(
                Choices.ToDictionary(c => c.Key, c => (Answer)new ChoiceAnswer(c.Value))
            )
        );
    }
}

public sealed class PlanResearch : Junction<string, Brief>
{
    public override Task<Brief> Run(string input)
    {
        CheckpointProbe.Note(nameof(PlanResearch));
        return Task.FromResult(new Brief(input));
    }
}

public sealed class SearchWeb : Junction<Brief, Findings>
{
    public override Task<Findings> Run(Brief input)
    {
        CheckpointProbe.Note(nameof(SearchWeb));
        return Task.FromResult(new Findings(input.Topic, "web"));
    }
}

public sealed class SearchPapers : Junction<Brief, Findings>
{
    public override Task<Findings> Run(Brief input)
    {
        CheckpointProbe.Note(nameof(SearchPapers));
        return Task.FromResult(new Findings(input.Topic, "papers"));
    }
}

public sealed class FetchFullTexts : Junction<Findings, CheckedFindings>
{
    public override Task<CheckedFindings> Run(Findings input)
    {
        CheckpointProbe.Note(nameof(FetchFullTexts));
        return Task.FromResult(
            new CheckedFindings(input.Topic, input.Source, 12, CheckpointProbe.Padding)
        );
    }
}

public sealed class ScoreFindings : Junction<CheckedFindings, Scored>
{
    public override Task<Scored> Run(CheckedFindings input)
    {
        CheckpointProbe.Note(nameof(ScoreFindings));
        return Task.FromResult(new Scored(input.Topic, input.Source, input.Pages / 2));
    }
}

public sealed class Summarize : Junction<Scored, string>
{
    public override Task<string> Run(Scored input)
    {
        CheckpointProbe.Note(nameof(Summarize));
        return Task.FromResult($"{input.Topic} from {input.Source}, scored {input.Score}");
    }
}

/// <summary>Leaves the step's Trax data context with work it has not committed.</summary>
public sealed class MakeDirty(IDataContext context) : Junction<CheckedFindings, CheckedFindings>
{
    public override async Task<CheckedFindings> Run(CheckedFindings input)
    {
        CheckpointProbe.Note(nameof(MakeDirty));

        switch (CheckpointProbe.Dirty)
        {
            case DirtyMode.UnsavedChange:
                await context.Track(
                    new ManifestGroup
                    {
                        Name = $"{CheckpointWriteNames.DirtyGroup}{Guid.NewGuid():N}",
                    }
                );
                break;
            case DirtyMode.OpenTransaction
                when context is DbContext { Database: var database } && database.IsSqlite():
                // Deferred, so it holds no lock until it writes: an immediate one would hold
                // SQLite's write lock and make the run's own terminal write wait it out.
                await database.OpenConnectionAsync();
                database.UseTransaction(
                    ((SqliteConnection)database.GetDbConnection()).BeginTransaction(deferred: true)
                );
                break;
            case DirtyMode.OpenTransaction:
                await context.BeginTransaction();
                break;
        }

        return input;
    }
}

public sealed class Conceal : Junction<Findings, SecretFindings>
{
    public override Task<SecretFindings> Run(Findings input)
    {
        CheckpointProbe.Note(nameof(Conceal));
        return Task.FromResult(new SecretFindings(input.Topic, "s3cr3t"));
    }
}

public sealed class Reveal : Junction<SecretFindings, string>
{
    public override Task<string> Run(SecretFindings input)
    {
        CheckpointProbe.Note(nameof(Reveal));
        return Task.FromResult(input.Topic);
    }
}

public sealed class OpenVaultLeft : Junction<Findings, Findings>
{
    public override Task<Findings> Run(Findings input)
    {
        CheckpointProbe.Note(nameof(OpenVaultLeft));
        return Task.FromResult(input);
    }
}

public sealed class OpenVaultRight : Junction<Findings, Findings>
{
    public override Task<Findings> Run(Findings input)
    {
        CheckpointProbe.Note(nameof(OpenVaultRight));
        return Task.FromResult(input);
    }
}

public sealed class StartBranchA : Junction<Brief, BranchA>
{
    public override Task<BranchA> Run(Brief input)
    {
        CheckpointProbe.Note(nameof(StartBranchA));
        return Task.FromResult(new BranchA(input.Topic));
    }
}

public sealed class StartBranchB : Junction<Brief, BranchB>
{
    public override Task<BranchB> Run(Brief input)
    {
        CheckpointProbe.Note(nameof(StartBranchB));
        return Task.FromResult(new BranchB(input.Topic));
    }
}

/// <summary>Holds its branch until the other one is here too, so both checkpoint at once.</summary>
public sealed class MeetA : Junction<BranchA, BranchA>
{
    public override Task<BranchA> Run(BranchA input)
    {
        CheckpointProbe.BothBranches?.Signal();
        CheckpointProbe.BothBranches?.Wait(TimeSpan.FromSeconds(30));
        return Task.FromResult(input);
    }
}

/// <inheritdoc cref="MeetA" />
public sealed class MeetB : Junction<BranchB, BranchB>
{
    public override Task<BranchB> Run(BranchB input)
    {
        CheckpointProbe.BothBranches?.Signal();
        CheckpointProbe.BothBranches?.Wait(TimeSpan.FromSeconds(30));
        return Task.FromResult(input);
    }
}

public sealed class Join : Junction<BranchA, string>
{
    public override Task<string> Run(BranchA input)
    {
        CheckpointProbe.Note(nameof(Join));
        return Task.FromResult(input.Topic);
    }
}

public static class CheckpointWriteNames
{
    /// <summary>The prefix of the manifest group a dirty step leaves unsaved.</summary>
    public const string DirtyGroup = "checkpoint-dirty-";
}

/// <summary>
/// <c>PlanResearch → Switch&lt;Brief, ResearchSource&gt; → FetchFullTexts →
/// Checkpoint&lt;CheckedFindings&gt; → ScoreFindings → Checkpoint&lt;Scored&gt; → Summarize</c>.
/// </summary>
public interface IResearchTrain : IServiceTrain<string, string>;

public sealed class ResearchTrain : ServiceTrain<string, string>, IResearchTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Switch<Brief, ResearchSource>(s =>
                s.When(ResearchSource.Web, w => w.Chain<SearchWeb>())
                    .When(ResearchSource.Papers, p => p.Chain<SearchPapers>())
            )
            .Chain<FetchFullTexts>()
            .Checkpoint<CheckedFindings>()
            .Chain<ScoreFindings>()
            .Checkpoint<Scored>()
            .Chain<Summarize>()
            .Resolve();
}

/// <summary>
/// The research chain, noting each call of its <c>Junctions()</c>: which instance, and whether it was
/// being read or run. Used by one test alone, so nothing else has read its chain first.
/// </summary>
public interface ICountingResearchTrain : IServiceTrain<string, string>;

public sealed class CountingResearchTrain : ServiceTrain<string, string>, ICountingResearchTrain
{
    public static System.Collections.Concurrent.ConcurrentQueue<(
        int Instance,
        bool Declaring
    )> Calls { get; } = new();

    protected override Task<Either<Exception, string>> Junctions()
    {
        Calls.Enqueue(
            (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this), IsDeclaringChain)
        );

        return Chain<PlanResearch>()
            .Switch<Brief, ResearchSource>(s =>
                s.When(ResearchSource.Web, w => w.Chain<SearchWeb>())
                    .When(ResearchSource.Papers, p => p.Chain<SearchPapers>())
            )
            .Chain<FetchFullTexts>()
            .Checkpoint<CheckedFindings>()
            .Chain<ScoreFindings>()
            .Checkpoint<Scored>()
            .Chain<Summarize>()
            .Resolve();
    }
}

/// <summary>The research chain with a step that leaves its data context dirty before the checkpoint.</summary>
public interface IDirtyResearchTrain : IServiceTrain<string, string>;

public sealed class DirtyResearchTrain : ServiceTrain<string, string>, IDirtyResearchTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Chain<SearchWebFromPlan>()
            .Chain<FetchFullTexts>()
            .Chain<MakeDirty>()
            .Checkpoint<CheckedFindings>()
            .Chain<ScoreFindings>()
            .Chain<Summarize>()
            .Resolve();
}

/// <summary>Searches without asking, for chains that route on nothing.</summary>
public sealed class SearchWebFromPlan : Junction<Brief, Findings>
{
    public override Task<Findings> Run(Brief input)
    {
        CheckpointProbe.Note(nameof(SearchWeb));
        return Task.FromResult(new Findings(input.Topic, "web"));
    }
}

/// <summary>Checkpoints a state that reaches a sensitive member.</summary>
public interface ISecretTrain : IServiceTrain<string, string>;

public sealed class SecretTrain : ServiceTrain<string, string>, ISecretTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Chain<SearchWebFromPlan>()
            .Chain<Conceal>()
            .Checkpoint<SecretFindings>()
            .Chain<Reveal>()
            .Resolve();
}

/// <summary>
/// Routes on a source and then on a sensitive vault, and checkpoints after both: the vault's track
/// is left out of the stored routes.
/// </summary>
public interface IVaultThenCheckpointTrain : IServiceTrain<string, string>;

public sealed class VaultThenCheckpointTrain
    : ServiceTrain<string, string>,
        IVaultThenCheckpointTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Switch<Brief, ResearchSource>(s =>
                s.When(ResearchSource.Web, w => w.Chain<SearchWeb>())
                    .When(ResearchSource.Papers, p => p.Chain<SearchPapers>())
            )
            .Switch<Findings, Vault>(s =>
                s.When(Vault.Left, l => l.Chain<OpenVaultLeft>())
                    .When(Vault.Right, r => r.Chain<OpenVaultRight>())
            )
            .Chain<FetchFullTexts>()
            .Checkpoint<CheckedFindings>()
            .Chain<ScoreFindings>()
            .Chain<Summarize>()
            .Resolve();
}

/// <summary>Checkpoints inside a track of the sensitive vault.</summary>
public interface ICheckpointInVaultTrain : IServiceTrain<string, string>;

public sealed class CheckpointInVaultTrain : ServiceTrain<string, string>, ICheckpointInVaultTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Chain<SearchWebFromPlan>()
            .Switch<Findings, Vault>(s =>
                s.When(
                        Vault.Left,
                        l =>
                            l.Chain<OpenVaultLeft>()
                                .Chain<FetchFullTexts>()
                                .Checkpoint<CheckedFindings>()
                    )
                    .When(Vault.Right, r => r.Chain<OpenVaultRight>().Chain<FetchFullTexts>())
            )
            .Chain<ScoreFindings>()
            .Chain<Summarize>()
            .Resolve();
}

/// <summary>Two branches that each checkpoint, at the same moment, then a join.</summary>
public interface ITwoBranchTrain : IServiceTrain<string, string>;

public sealed class TwoBranchTrain : ServiceTrain<string, string>, ITwoBranchTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<StartBranchA>().Chain<MeetA>().Checkpoint<BranchA>())
                    .Branch("b", b => b.Chain<StartBranchB>().Chain<MeetB>().Checkpoint<BranchB>())
            )
            .Chain<Join>()
            .Resolve();
}
