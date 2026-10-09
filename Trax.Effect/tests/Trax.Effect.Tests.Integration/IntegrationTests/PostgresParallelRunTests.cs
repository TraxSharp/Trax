using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A service train whose junctions run in <c>Parallel</c> branches, against Postgres with junction
/// events, junction progress and decision recording all on: the branches' junctions write side by
/// side without sharing a context, two branches asking one question each get a row and replay
/// their own answer, and a run cancelled or failed while its branches run is recorded as one run.
/// <see cref="CheckTraxInvariantsAttribute"/> checks the database after each test.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PostgresParallelRunTests
{
    private ServiceProvider _provider = null!;

    private static readonly PgParDecider Decider = new();

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var connectionString = TestPostgres.WithPort(
            configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        );

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDecider>(Decider);
        services.AddSingleton<IFailureClassifier, PgParClassifier>();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .UsePostgres(connectionString)
                    .AddJunctionEvents()
                    .AddJunctionProgress()
                    .AddDecisionRecording()
            )
        );
        services
            .AddScopedTraxRoute<IPgParBusyTrain, PgParBusyTrain>()
            .AddScopedTraxRoute<IPgParAskEverywhereTrain, PgParAskEverywhereTrain>()
            .AddScopedTraxRoute<IPgParWaitTrain, PgParWaitTrain>()
            .AddScopedTraxRoute<IPgParFlagTrain, PgParFlagTrain>()
            .AddScopedTraxRoute<IPgParFailTrain, PgParFailTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [SetUp]
    public void Reset()
    {
        PgParSignals.Reset();
        Decider.Script();
    }

    [Test]
    public async Task Branch_junctions_write_their_events_and_progress_side_by_side()
    {
        var runs = new List<long>();

        // Several runs at once as well, so writes from different runs interleave too.
        var trains = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Run<IPgParBusyTrain>()));

        foreach (var (train, output) in trains)
        {
            output.Should().Be("a=4, b=40");
            runs.Add(train.Metadata!.Id);

            var row = await RunRow(train.Metadata.Id);
            row.TrainState.Should().Be(TrainState.Completed);
            row.CurrentlyRunningJunction.Should().BeNull("every junction has ended");
            row.JunctionStartedAt.Should().BeNull();

            var steps = await Rows(train.Metadata.Id);
            steps
                .Should()
                .HaveCount(10)
                .And.OnlyContain(r => r.State == JunctionRunState.Completed);
            steps.Select(r => r.Position).Should().OnlyHaveUniqueItems();
            steps
                .Where(r => r.BranchPath == "Parallel#0/a")
                .Select(r => r.NodeId)
                .Should()
                .BeEquivalentTo(
                    "Parallel#0/a/PgParScoreA#0",
                    "Parallel#0/a/PgParBumpA#0",
                    "Parallel#0/a/PgParBumpA#1",
                    "Parallel#0/a/PgParBumpA#2"
                );
            steps
                .Where(r => r.BranchPath == "Parallel#0/b")
                .Select(r => r.NodeId)
                .Should()
                .BeEquivalentTo(
                    "Parallel#0/b/PgParScoreB#0",
                    "Parallel#0/b/PgParBumpB#0",
                    "Parallel#0/b/PgParBumpB#1",
                    "Parallel#0/b/PgParBumpB#2"
                );
            steps
                .Where(r => r.BranchPath == null)
                .Select(r => r.NodeId)
                .Should()
                .BeEquivalentTo("PgParStart#0", "PgParCombine#0");
        }

        PgParSignals.Met.Should().BeTrue("each run's branches waited for each other");
        await Delete([.. runs]);
    }

    [Test]
    public async Task Branches_asking_one_question_each_get_a_row_and_a_requeue_replays_each()
    {
        Decider.Script(
            byBranch: new()
            {
                [""] = [PgParLane.Express, PgParLane.Ground],
                ["Parallel#0/a"] = [PgParLane.Ground],
                ["Parallel#0/b"] = [PgParLane.Express],
            }
        );

        var (original, _) = await Run<IPgParAskEverywhereTrain>();

        var recorded = await Recorded(original.Metadata!.Id);
        recorded
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
        var taken = PgParTook.Of(original.Metadata.Id);

        Decider.Script(always: PgParLane.Express);
        var (requeued, _) = await Run<IPgParAskEverywhereTrain>(
            replayDecisionsOf: original.Metadata.Id
        );

        Decider.Asked.Should().Be(0);
        PgParTook.Of(requeued.Metadata!.Id).Should().BeEquivalentTo(taken);
        (await Recorded(requeued.Metadata.Id))
            .Should()
            .HaveCount(4)
            .And.OnlyContain(r => r.Replayed);

        await Delete(original.Metadata.Id, requeued.Metadata.Id);
    }

    [Test]
    public async Task A_run_cancelled_by_its_caller_while_branches_run_is_recorded_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        using var scope = _provider.CreateScope();
        var train = Train<IPgParWaitTrain>(scope);

        var running = train.Run(new PgParOrder("o"), cancel.Token);
        await PgParSignals.BothWaiting.WaitAsync(TimeSpan.FromSeconds(30));
        await cancel.CancelAsync();

        await FluentActions
            .Awaiting(() => running)
            .Should()
            .ThrowAsync<OperationCanceledException>();
        (await RunRow(train.Metadata!.Id)).TrainState.Should().Be(TrainState.Cancelled);

        await Delete(train.Metadata.Id);
    }

    [Test]
    public async Task A_run_cancelled_through_its_cancel_flag_in_one_branch_is_recorded_cancelled()
    {
        using var scope = _provider.CreateScope();
        var train = Train<IPgParFlagTrain>(scope);

        var running = train.Run(new PgParOrder("o"));
        await PgParSignals.BothWaiting.WaitAsync(TimeSpan.FromSeconds(30));

        // What the dashboard's cancel and the scheduler's job timeout write for a run on another
        // host. Branch a's next junction reads it before it runs.
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using (var context = await factory.CreateDbContextAsync(CancellationToken.None))
            await context
                .Metadatas.Where(m => m.Id == train.Metadata!.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.CancellationRequested, true));
        PgParSignals.Release();

        await FluentActions
            .Awaiting(() => running)
            .Should()
            .ThrowAsync<OperationCanceledException>();

        var row = await RunRow(train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Cancelled);
        row.FailureClass.Should().Be(FailureClass.Unclassified, "a cancellation is not a failure");

        await Delete(train.Metadata.Id);
    }

    [Test]
    public async Task A_failed_branch_fails_the_run_with_the_combined_class_of_its_branches()
    {
        using var scope = _provider.CreateScope();
        var train = Train<IPgParFailTrain>(scope);

        await FluentActions
            .Awaiting(() => train.Run(new PgParOrder("o")))
            .Should()
            .ThrowAsync<BranchesFailedException>();

        var row = await RunRow(train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Failed);
        row.FailureClass.Should()
            .Be(FailureClass.Permanent, "one branch's failure is permanent, the other transient");
        row.FailureException.Should().Be(nameof(BranchesFailedException));
        row.FailureJunction.Should().MatchRegex("^Parallel#0/[ab]:PgParBreak[AB]$");

        await Delete(train.Metadata.Id);
    }

    private static ServiceTrain<PgParOrder, string> Train<TTrain>(IServiceScope scope)
        where TTrain : class, IServiceTrain<PgParOrder, string> =>
        (ServiceTrain<PgParOrder, string>)
            (object)scope.ServiceProvider.GetRequiredService<TTrain>();

    private async Task<(ServiceTrain<PgParOrder, string> Train, string Output)> Run<TTrain>(
        long? replayDecisionsOf = null
    )
        where TTrain : class, IServiceTrain<PgParOrder, string>
    {
        using var scope = _provider.CreateScope();
        var train = Train<TTrain>(scope);
        var input = new PgParOrder("o");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(TTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );

        var output = await train.Run(input, metadata);
        await _provider.GetRequiredService<JunctionRunWriter>().FlushAsync();
        return (train, output);
    }

    private async Task<Metadata> RunRow(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == metadataId);
    }

    private async Task<List<JunctionRun>> Rows(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.JunctionRuns.AsNoTracking().ForRun(metadataId).ToListAsync();
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

    private async Task Delete(params long[] metadataIds)
    {
        // A step still queued for the writer would insert under the delete's cascade.
        await _provider.GetRequiredService<JunctionRunWriter>().FlushAsync();

        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        await context.Metadatas.Where(m => metadataIds.Contains(m.Id)).ExecuteDeleteAsync();
    }
}

public sealed record PgParOrder(string Id);

public sealed record PgParA(int Score);

public sealed record PgParB(int Score);

[Asks("Which lane does this order take?")]
public enum PgParLane
{
    Express,
    Ground,
}

/// <summary>
/// Lets the branches of one run meet, so a run that passes ran them side by side, and lets a test
/// hold a branch until it has done something to the run.
/// </summary>
internal static class PgParSignals
{
    private static readonly ConcurrentDictionary<
        long,
        (TaskCompletionSource A, TaskCompletionSource B)
    > Meetings = new();
    private static int _waiting;
    private static TaskCompletionSource _bothWaiting = New();
    private static TaskCompletionSource _release = New();

    public static bool Met { get; private set; }

    public static Task BothWaiting => _bothWaiting.Task;

    public static void Reset()
    {
        Meetings.Clear();
        _waiting = 0;
        _bothWaiting = New();
        _release = New();
        Met = false;
    }

    public static async Task Arrive(long run, bool isA)
    {
        var (a, b) = Meetings.GetOrAdd(run, _ => (New(), New()));
        (isA ? a : b).TrySetResult();
        await (isA ? b : a).Task.WaitAsync(TimeSpan.FromSeconds(30));
        Met = true;
    }

    public static void Waiting()
    {
        if (Interlocked.Increment(ref _waiting) == 2)
            _bothWaiting.TrySetResult();
    }

    public static Task Released => _release.Task;

    public static void Release() => _release.TrySetResult();

    /// <summary>Completes only when <paramref name="token"/> is cancelled, by throwing.</summary>
    public static async Task UntilCancelled(CancellationToken token)
    {
        var cancelled = New();
        await using (token.Register(() => cancelled.TrySetCanceled(token)))
            await cancelled.Task;
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Answers by the branch asking, in order, and counts what it was asked.</summary>
internal sealed class PgParDecider : IDecider
{
    private Dictionary<string, Queue<PgParLane>> _byBranch = [];
    private PgParLane _always;
    private int _asked;

    public int Asked => _asked;

    public void Script(
        Dictionary<string, PgParLane[]>? byBranch = null,
        PgParLane always = PgParLane.Ground
    )
    {
        _byBranch = (byBranch ?? []).ToDictionary(b => b.Key, b => new Queue<PgParLane>(b.Value));
        _always = always;
        _asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);

        var branch = ChainGraph.CurrentBranchPath ?? "";
        PgParLane lane;
        lock (_byBranch)
            lane =
                _byBranch.TryGetValue(branch, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : _always;

        return new ScriptedDecider().Choose(lane).Decide(request, cancellationToken);
    }
}

internal sealed class PgParClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            TimeoutException => FailureClass.Transient,
            InvalidOperationException => FailureClass.Permanent,
            _ => null,
        };
}

internal static class PgParTook
{
    private static readonly ConcurrentQueue<(long Run, string Branch, string Track)> Taken = new();

    public static void Note(long run, string track) =>
        Taken.Enqueue((run, ChainGraph.CurrentBranchPath ?? "", track));

    public static List<(string Branch, string Track)> Of(long run) =>
        Taken.Where(t => t.Run == run).Select(t => (t.Branch, t.Track)).ToList();
}

public class PgParStart : EffectJunction<PgParOrder, PgParOrder>
{
    public override Task<PgParOrder> Run(PgParOrder input) => Task.FromResult(input);
}

public class PgParScoreA : EffectJunction<PgParOrder, PgParA>
{
    public override async Task<PgParA> Run(PgParOrder input)
    {
        await PgParSignals.Arrive(Metadata!.TrainMetadataId, isA: true);
        return new PgParA(1);
    }
}

public class PgParScoreB : EffectJunction<PgParOrder, PgParB>
{
    public override async Task<PgParB> Run(PgParOrder input)
    {
        await PgParSignals.Arrive(Metadata!.TrainMetadataId, isA: false);
        return new PgParB(10);
    }
}

public class PgParBumpA : EffectJunction<PgParA, PgParA>
{
    public override async Task<PgParA> Run(PgParA input)
    {
        await Task.Yield();
        return input with { Score = input.Score + 1 };
    }
}

public class PgParBumpB : EffectJunction<PgParB, PgParB>
{
    public override async Task<PgParB> Run(PgParB input)
    {
        await Task.Yield();
        return input with { Score = input.Score + 10 };
    }
}

public class PgParCombine : EffectJunction<(PgParA, PgParB), string>
{
    public override Task<string> Run((PgParA, PgParB) input) =>
        Task.FromResult($"a={input.Item1.Score}, b={input.Item2.Score}");
}

public class PgParTookExpress : EffectJunction<PgParOrder, Unit>
{
    public override Task<Unit> Run(PgParOrder input)
    {
        PgParTook.Note(Metadata!.TrainMetadataId, nameof(PgParLane.Express));
        return Task.FromResult(Unit.Default);
    }
}

public class PgParTookGround : EffectJunction<PgParOrder, Unit>
{
    public override Task<Unit> Run(PgParOrder input)
    {
        PgParTook.Note(Metadata!.TrainMetadataId, nameof(PgParLane.Ground));
        return Task.FromResult(Unit.Default);
    }
}

public class PgParFinish : EffectJunction<PgParOrder, string>
{
    public override Task<string> Run(PgParOrder input) => Task.FromResult("done");
}

/// <summary>Waits for its branch's token, which only a cancellation ends.</summary>
public class PgParWaitA : EffectJunction<PgParOrder, PgParA>
{
    public override async Task<PgParA> Run(PgParOrder input)
    {
        PgParSignals.Waiting();
        await PgParSignals.UntilCancelled(CancellationToken);
        return new PgParA(0);
    }
}

public class PgParWaitB : EffectJunction<PgParOrder, PgParB>
{
    public override async Task<PgParB> Run(PgParOrder input)
    {
        PgParSignals.Waiting();
        await PgParSignals.UntilCancelled(CancellationToken);
        return new PgParB(0);
    }
}

/// <summary>Says it is waiting, then holds its branch until the test releases it.</summary>
public class PgParHoldA : EffectJunction<PgParOrder, PgParA>
{
    public override async Task<PgParA> Run(PgParOrder input)
    {
        PgParSignals.Waiting();
        await PgParSignals.Released.WaitAsync(TimeSpan.FromSeconds(30));
        return new PgParA(0);
    }
}

public class PgParBreakA : EffectJunction<PgParOrder, PgParA>
{
    public override async Task<PgParA> Run(PgParOrder input)
    {
        await Task.Yield();
        throw new TimeoutException("a timed out");
    }
}

public class PgParBreakB : EffectJunction<PgParOrder, PgParB>
{
    public override async Task<PgParB> Run(PgParOrder input)
    {
        await Task.Yield();
        throw new InvalidOperationException("b broke");
    }
}

public interface IPgParBusyTrain : IServiceTrain<PgParOrder, string>;

/// <summary>Two branches of four junctions each, whose first junctions meet.</summary>
public class PgParBusyTrain : ServiceTrain<PgParOrder, string>, IPgParBusyTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PgParStart>()
            .Parallel(p =>
                p.Branch(
                        "a",
                        b =>
                            b.Chain<PgParScoreA>()
                                .Chain<PgParBumpA>()
                                .Chain<PgParBumpA>()
                                .Chain<PgParBumpA>()
                    )
                    .Branch(
                        "b",
                        b =>
                            b.Chain<PgParScoreB>()
                                .Chain<PgParBumpB>()
                                .Chain<PgParBumpB>()
                                .Chain<PgParBumpB>()
                    )
            )
            .Chain<PgParCombine>()
            .Resolve();
}

public interface IPgParAskEverywhereTrain : IServiceTrain<PgParOrder, string>;

/// <summary>Asks one question before the fork, in each of two branches, and after the join.</summary>
public class PgParAskEverywhereTrain : ServiceTrain<PgParOrder, string>, IPgParAskEverywhereTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<PgParOrder, PgParLane>(Lanes)
            .Parallel(p =>
                p.Branch("a", b => b.Switch<PgParOrder, PgParLane>(Lanes))
                    .Branch("b", b => b.Switch<PgParOrder, PgParLane>(Lanes))
            )
            .Switch<PgParOrder, PgParLane>(Lanes)
            .Chain<PgParFinish>()
            .Resolve();

    private static Tracks<PgParOrder, string, PgParLane> Lanes(
        Tracks<PgParOrder, string, PgParLane> tracks
    ) =>
        tracks
            .When(PgParLane.Express, t => t.Chain<PgParTookExpress>())
            .When(PgParLane.Ground, t => t.Chain<PgParTookGround>());
}

public interface IPgParWaitTrain : IServiceTrain<PgParOrder, string>;

public class PgParWaitTrain : ServiceTrain<PgParOrder, string>, IPgParWaitTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PgParStart>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<PgParWaitA>()).Branch("b", b => b.Chain<PgParWaitB>())
            )
            .Chain<PgParCombine>()
            .Resolve();
}

public interface IPgParFlagTrain : IServiceTrain<PgParOrder, string>;

/// <summary>Branch a is held, then runs another junction; branch b waits to be cancelled.</summary>
public class PgParFlagTrain : ServiceTrain<PgParOrder, string>, IPgParFlagTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PgParStart>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<PgParHoldA>().Chain<PgParBumpA>())
                    .Branch("b", b => b.Chain<PgParWaitB>())
            )
            .Chain<PgParCombine>()
            .Resolve();
}

public interface IPgParFailTrain : IServiceTrain<PgParOrder, string>;

/// <summary>Branch a fails transient and branch b permanent, by the classifier; both are waited for.</summary>
public class PgParFailTrain : ServiceTrain<PgParOrder, string>, IPgParFailTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PgParStart>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<PgParBreakA>())
                    .Branch("b", b => b.Chain<PgParBreakB>())
                    .OnFailure(BranchFailurePolicy.WaitForAll)
            )
            .Chain<PgParCombine>()
            .Resolve();
}
