using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// A service train whose questions are asked in <c>Parallel</c> branches, against Sqlite: the
/// rebuilt <c>decision</c> table keys an asking by its branch, so two branches asking one question
/// each get a row and a requeue replays each, and each step's row carries its branch.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SqliteParallelRunTests
{
    private ServiceProvider _provider = null!;

    private static readonly LiteParDecider Decider = new();

    private string _dbPath = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_parallel_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDecider>(Decider);
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .UseSqlite($"Data Source={_dbPath}")
                    .AddJunctionEvents()
                    .AddDecisionRecording()
            )
        );
        services.AddScopedTraxRoute<ILiteParAskEverywhereTrain, LiteParAskEverywhereTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider()
    {
        await _provider.DisposeAsync();

        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(path))
                File.Delete(path);
    }

    [Test]
    public async Task Branches_asking_one_question_each_get_a_row_and_a_requeue_replays_each()
    {
        Decider.Script(
            new()
            {
                [""] = [LiteParLane.Express, LiteParLane.Ground],
                ["Parallel#0/a"] = [LiteParLane.Ground],
                ["Parallel#0/b"] = [LiteParLane.Express],
            }
        );

        var original = await Run();

        (await Recorded(original))
            .Select(r => (r.BranchPath, r.Occurrence, r.Tracks().Single()))
            .Should()
            .BeEquivalentTo(
                [
                    ("", 0, "Express"),
                    ("Parallel#0/a", 1, "Ground"),
                    ("Parallel#0/b", 1, "Express"),
                    ("", 1, "Ground"),
                ],
                "the branches count on from the fork, and the unique key tells them apart by branch"
            );

        Decider.Script([]);
        var requeued = await Run(replayDecisionsOf: original);

        Decider.Asked.Should().Be(0);
        LiteParTook.Of(requeued).Should().BeEquivalentTo(LiteParTook.Of(original));
        (await Recorded(requeued)).Should().HaveCount(4).And.OnlyContain(r => r.Replayed);
    }

    [Test]
    public async Task Each_step_row_carries_the_branch_it_ran_in()
    {
        Decider.Script([]);

        var run = await Run();

        var rows = await Rows(run);
        rows.Where(r => r.Name == nameof(LiteParTookGround))
            .Select(r => r.BranchPath)
            .Should()
            .BeEquivalentTo([null, "Parallel#0/a", "Parallel#0/b", null]);
        rows.Single(r => r.Name == nameof(LiteParFinish)).BranchPath.Should().BeNull();
    }

    private async Task<long> Run(long? replayDecisionsOf = null)
    {
        using var scope = _provider.CreateScope();
        var train =
            (ServiceTrain<LiteParOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<ILiteParAskEverywhereTrain>();
        var input = new LiteParOrder("o");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ILiteParAskEverywhereTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );

        await train.Run(input, metadata);
        await _provider.GetRequiredService<JunctionRunWriter>().FlushAsync();
        return train.Metadata!.Id;
    }

    private async Task<List<Models.RecordedDecision.RecordedDecision>> Recorded(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .ToListAsync();
    }

    private async Task<List<JunctionRun>> Rows(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.JunctionRuns.AsNoTracking().ForRun(metadataId).ToListAsync();
    }
}

public sealed record LiteParOrder(string Id);

[Asks("Which lane does this order take?")]
public enum LiteParLane
{
    Express,
    Ground,
}

/// <summary>Answers by the branch asking, in order, Ground once a branch's script runs out.</summary>
internal sealed class LiteParDecider : IDecider
{
    private Dictionary<string, Queue<LiteParLane>> _byBranch = [];
    private int _asked;

    public int Asked => _asked;

    public void Script(Dictionary<string, LiteParLane[]> byBranch)
    {
        _byBranch = byBranch.ToDictionary(b => b.Key, b => new Queue<LiteParLane>(b.Value));
        _asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);

        var branch = ChainGraph.CurrentBranchPath ?? "";
        LiteParLane lane;
        lock (_byBranch)
            lane =
                _byBranch.TryGetValue(branch, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : LiteParLane.Ground;

        return new ScriptedDecider().Choose(lane).Decide(request, cancellationToken);
    }
}

internal static class LiteParTook
{
    private static readonly ConcurrentQueue<(long Run, string Branch, string Track)> Taken = new();

    public static void Note(long run, string track) =>
        Taken.Enqueue((run, ChainGraph.CurrentBranchPath ?? "", track));

    public static List<(string Branch, string Track)> Of(long run) =>
        Taken.Where(t => t.Run == run).Select(t => (t.Branch, t.Track)).ToList();
}

public class LiteParTookExpress : EffectJunction<LiteParOrder, Unit>
{
    public override Task<Unit> Run(LiteParOrder input)
    {
        LiteParTook.Note(Metadata!.TrainMetadataId, nameof(LiteParLane.Express));
        return Task.FromResult(Unit.Default);
    }
}

public class LiteParTookGround : EffectJunction<LiteParOrder, Unit>
{
    public override Task<Unit> Run(LiteParOrder input)
    {
        LiteParTook.Note(Metadata!.TrainMetadataId, nameof(LiteParLane.Ground));
        return Task.FromResult(Unit.Default);
    }
}

public class LiteParFinish : EffectJunction<LiteParOrder, string>
{
    public override Task<string> Run(LiteParOrder input) => Task.FromResult("done");
}

public interface ILiteParAskEverywhereTrain : IServiceTrain<LiteParOrder, string>;

/// <summary>Asks one question before the fork, in each of two branches, and after the join.</summary>
public class LiteParAskEverywhereTrain
    : ServiceTrain<LiteParOrder, string>,
        ILiteParAskEverywhereTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<LiteParOrder, LiteParLane>(Lanes)
            .Parallel(p =>
                p.Branch("a", b => b.Switch<LiteParOrder, LiteParLane>(Lanes))
                    .Branch("b", b => b.Switch<LiteParOrder, LiteParLane>(Lanes))
            )
            .Switch<LiteParOrder, LiteParLane>(Lanes)
            .Chain<LiteParFinish>()
            .Resolve();

    private static Tracks<LiteParOrder, string, LiteParLane> Lanes(
        Tracks<LiteParOrder, string, LiteParLane> tracks
    ) =>
        tracks
            .When(LiteParLane.Express, t => t.Chain<LiteParTookExpress>())
            .When(LiteParLane.Ground, t => t.Chain<LiteParTookGround>());
}
