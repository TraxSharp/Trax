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
                .Where(r => string.Equals(r.BranchPath, "Parallel#0/a", StringComparison.Ordinal))
                .Select(r => r.NodeId)
                .Should()
                .BeEquivalentTo(
                    "Parallel#0/a/PgParScoreA#0",
                    "Parallel#0/a/PgParBumpA#0",
                    "Parallel#0/a/PgParBumpA#1",
                    "Parallel#0/a/PgParBumpA#2"
                );
            steps
                .Where(r => string.Equals(r.BranchPath, "Parallel#0/b", StringComparison.Ordinal))
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
            byBranch: new(StringComparer.Ordinal)
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
        var scope = _provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = Train<IPgParWaitTrain>(scope);

        var running = train.Run(new PgParOrder("o"), cancel.Token);
        await PgParSignals.BothWaiting.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
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
        var scope = _provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = Train<IPgParFlagTrain>(scope);

        var running = train.Run(new PgParOrder("o"), CancellationToken.None);
        await PgParSignals.BothWaiting.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        // What the dashboard's cancel and the scheduler's job timeout write for a run on another
        // host. Branch a's next junction reads it before it runs.
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory.CreateDbContextAsync(CancellationToken.None);
        await using (context.ConfigureAwait(false))
            await context
                .Metadatas.Where(m => m.Id == train.Metadata!.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(m => m.CancellationRequested, valueExpression: true),
                    cancellationToken: CancellationToken.None
                );
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
        var scope = _provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = Train<IPgParFailTrain>(scope);

        await FluentActions
            .Awaiting(() => train.Run(new PgParOrder("o"), CancellationToken.None))
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
        var scope = _provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
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

        var output = await train.Run(input, metadata, CancellationToken.None).ConfigureAwait(false);
        await _provider
            .GetRequiredService<JunctionRunWriter>()
            .FlushAsync(CancellationToken.None)
            .ConfigureAwait(false);
        return (train, output);
    }

    private async Task<Metadata> RunRow(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.Id == metadataId)
            .ConfigureAwait(false);
    }

    private async Task<List<JunctionRun>> Rows(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context
            .JunctionRuns.AsNoTracking()
            .ForRun(metadataId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    private async Task<List<Models.RecordedDecision.RecordedDecision>> Recorded(long metadataId)
    {
        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    private async Task Delete(params long[] metadataIds)
    {
        // A step still queued for the writer would insert under the delete's cascade.
        await _provider.GetRequiredService<JunctionRunWriter>().FlushAsync().ConfigureAwait(false);

        var factory = _provider.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        await context
            .Metadatas.Where(m => metadataIds.Contains(m.Id))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }
}
