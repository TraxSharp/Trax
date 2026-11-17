using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// Junction progress writes the run's two progress columns and nothing else. It used to save
/// through the run's effect runner, which committed everything the run had tracked so far after
/// every junction and saved every other effect provider each time; a run's own writes now commit
/// once, when it finishes. A run that ends while a junction is still running, because it was
/// cancelled or a sibling branch failed, leaves no progress behind on any data provider.
///
/// <para>Enforces <c>docs/adr/0021-a-runs-tracked-writes-commit-once-when-it-finishes.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0021-a-runs-tracked-writes-commit-once-when-it-finishes.md")]
[NonParallelizable]
public class JunctionProgressWritesOnlyProgressTests
{
    /// <summary>The data providers the progress columns are checked on.</summary>
    public enum Store
    {
        InMemory,
        Sqlite,
        Postgres,
    }

    private const string Adr =
        "docs/adr/0021-a-runs-tracked-writes-commit-once-when-it-finishes.md";

    [Test]
    public async Task Progress_does_not_save_the_runs_other_effects_after_each_junction()
    {
        var counter = new SaveCounter();
        var provider = Build(counter, progress: true);
        await using var providerDisposal = provider.ConfigureAwait(false);
        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = (ThreeJunctionTrain)
            scope.ServiceProvider.GetRequiredService<IThreeJunctionTrain>();

        await train.Run(Unit.Default, CancellationToken.None);

        var withoutProgress = await SavesWithoutProgress();
        counter
            .Saves.Should()
            .Be(
                withoutProgress,
                "junction progress writes its own columns; it must not save the run's other "
                    + $"effects once per junction ({Adr})"
            );
    }

    [Test]
    public async Task Progress_is_visible_in_the_store_while_the_junction_runs()
    {
        var provider = Build(new SaveCounter(), progress: true);
        await using var providerDisposal = provider.ConfigureAwait(false);
        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = (WatchedTrain)scope.ServiceProvider.GetRequiredService<IWatchedTrain>();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        WatchedTrain.Seen = null;
        WatchedTrain.Factory = factory;

        await train.Run(Unit.Default, CancellationToken.None);

        WatchedTrain
            .Seen.Should()
            .Be(
                nameof(WatchedTrain.ReadsItsOwnProgress),
                "the column is written before the junction runs"
            );

        var context = (IDataContext)factory.Create();
        await using var contextDisposal = context.ConfigureAwait(false);
        var row = await context
            .Metadatas.AsNoTracking()
            .SingleAsync(
                m => m.Id == train.Metadata!.Id,
                cancellationToken: CancellationToken.None
            );
        row.CurrentlyRunningJunction.Should().BeNull("the run's final write clears it");
        row.JunctionStartedAt.Should().BeNull();
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    [TestCase(Store.Postgres)]
    public async Task A_run_cancelled_mid_junction_leaves_no_progress(Store store)
    {
        var row = await RunUntilCancelled<ICancelledTrain>(store, waiting: 1);

        row.TrainState.Should().Be(TrainState.Cancelled);
        row.CurrentlyRunningJunction.Should()
            .BeNull("the run's final write clears the junction that never ended");
        row.JunctionStartedAt.Should().BeNull();
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    [TestCase(Store.Postgres)]
    public async Task A_run_cancelled_while_its_branches_run_leaves_no_progress(Store store)
    {
        var row = await RunUntilCancelled<ICancelledParallelTrain>(store, waiting: 2);

        row.TrainState.Should().Be(TrainState.Cancelled);
        row.CurrentlyRunningJunction.Should()
            .BeNull("the run's final write clears the junctions that never ended");
        row.JunctionStartedAt.Should().BeNull();
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    [TestCase(Store.Postgres)]
    public async Task A_run_whose_branch_cancelled_its_sibling_leaves_no_progress(Store store)
    {
        var host = ProgressHost.Create(store);
        await using var hostDisposal = host.ConfigureAwait(false);
        Waits.Reset(expected: 1);
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train =
            (ServiceTrain<Unit, Unit>)
                (object)scope.ServiceProvider.GetRequiredService<ICancelSiblingsTrain>();

        await FluentActions
            .Awaiting(() => train.Run(Unit.Default, CancellationToken.None))
            .Should()
            .ThrowAsync<BranchesFailedException>();

        var row = await host.Row(train.Metadata!.Id);
        await host.Delete(train.Metadata.Id);

        row.TrainState.Should().Be(TrainState.Failed);
        row.CurrentlyRunningJunction.Should()
            .BeNull(
                "the sibling cancelled mid-junction never ended, and the final write clears it"
            );
        row.JunctionStartedAt.Should().BeNull();
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    [TestCase(Store.Postgres)]
    public async Task A_failed_run_commits_its_tracked_row_only_when_it_finishes(Store store)
    {
        var host = ProgressHost.Create(store);
        await using var hostDisposal = host.ConfigureAwait(false);
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = (TracksThenFailsTrain)
            scope.ServiceProvider.GetRequiredService<ITracksThenFailsTrain>();
        var group = $"progress-failed-{Guid.NewGuid():N}";
        TracksThenFailsTrain.Instance = train;
        TracksThenFailsTrain.Factory =
            host.Services.GetRequiredService<IDataContextProviderFactory>();
        TracksThenFailsTrain.Group = group;
        TracksThenFailsTrain.Seen.Clear();
        TracksThenFailsTrain.GroupSeenMidRun = null;

        await FluentActions
            .Awaiting(() => train.Run(Unit.Default, CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>();

        var row = await host.Row(train.Metadata!.Id);
        var committed = await host.GroupExists(group);
        await host.DeleteGroup(group);
        await host.Delete(train.Metadata.Id);

        row.TrainState.Should().Be(TrainState.Failed);
        TracksThenFailsTrain
            .Seen.Should()
            .Equal(
                [nameof(TracksARow), nameof(FailsAfterTheRow)],
                "progress writes each junction's name to the store before the junction runs"
            );
        TracksThenFailsTrain
            .GroupSeenMidRun.Should()
            .BeFalse(
                "writing the second junction's progress must not commit the row the first "
                    + $"junction tracked ({Adr})"
            );
        committed
            .Should()
            .BeTrue(
                "the run's terminal write commits what it tracked, whatever the outcome "
                    + $"({Adr})"
            );
    }

    private static async Task<Models.Metadata.Metadata> RunUntilCancelled<TTrain>(
        Store store,
        int waiting
    )
        where TTrain : class, IServiceTrain<Unit, Unit>
    {
        var host = ProgressHost.Create(store);
        await using var hostDisposal = host.ConfigureAwait(false);
        Waits.Reset(expected: waiting);
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train =
            (ServiceTrain<Unit, Unit>)(object)scope.ServiceProvider.GetRequiredService<TTrain>();
        using var cancel = new CancellationTokenSource();

        var running = train.Run(Unit.Default, cancel.Token);
        await Waits
            .AllWaiting.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None)
            .ConfigureAwait(false);

        // The column is set while the junction runs, so the test proves the clear rather than
        // a column that was never written.
        var during = await host.Row(train.Metadata!.Id).ConfigureAwait(false);
        during.CurrentlyRunningJunction.Should().NotBeNull("the junction is running");

        await cancel.CancelAsync().ConfigureAwait(false);
        await FluentActions
            .Awaiting(() => running)
            .Should()
            .ThrowAsync<OperationCanceledException>()
            .ConfigureAwait(false);

        var row = await host.Row(train.Metadata.Id).ConfigureAwait(false);
        await host.Delete(train.Metadata.Id).ConfigureAwait(false);
        return row;
    }

    private static async Task<int> SavesWithoutProgress()
    {
        var counter = new SaveCounter();
        var provider = Build(counter, progress: false);
        await using var providerDisposal = provider.ConfigureAwait(false);
        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = (ThreeJunctionTrain)
            scope.ServiceProvider.GetRequiredService<IThreeJunctionTrain>();

        await train.Run(Unit.Default, CancellationToken.None).ConfigureAwait(false);

        return counter.Saves;
    }

    private static ServiceProvider Build(SaveCounter counter, bool progress)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
            {
                var configured = effects.UseInMemory().AddEffect(counter);
                return progress ? configured.AddJunctionProgress() : configured;
            })
        );
        services.AddScopedTraxRoute<IThreeJunctionTrain, ThreeJunctionTrain>();
        services.AddScopedTraxRoute<IWatchedTrain, WatchedTrain>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A host with junction progress over one data provider. Postgres is the shared test database,
    /// so a test deletes the runs it made; SQLite is a file of its own, dropped with the host.
    /// </summary>
    private sealed class ProgressHost(ServiceProvider services, string? sqliteFile)
        : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;

        public static ProgressHost Create(Store store)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            string? sqliteFile = null;
            services.AddTrax(trax =>
                trax.AddEffects(effects =>
                    (
                        store switch
                        {
                            Store.Postgres => effects.UsePostgres(PostgresConnection()),
                            Store.Sqlite => effects.UseSqlite(
                                $"Data Source={sqliteFile = Path.Combine(Path.GetTempPath(), $"trax_progress_{Guid.NewGuid():N}.db")}"
                            ),
                            _ => effects.UseInMemory(),
                        }
                    ).AddJunctionProgress()
                )
            );
            services
                .AddScopedTraxRoute<ICancelledTrain, CancelledTrain>()
                .AddScopedTraxRoute<ICancelledParallelTrain, CancelledParallelTrain>()
                .AddScopedTraxRoute<ICancelSiblingsTrain, CancelSiblingsTrain>()
                .AddScopedTraxRoute<ITracksThenFailsTrain, TracksThenFailsTrain>();
            return new ProgressHost(services.BuildServiceProvider(), sqliteFile);
        }

        private static string PostgresConnection() =>
            TestPostgres.WithPort(
                new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false)
                    .Build()
                    .GetRequiredSection("Configuration")["DatabaseConnectionString"]!
            );

        public async Task<Models.Metadata.Metadata> Row(long id)
        {
            var context = await Services
                .GetRequiredService<IDataContextProviderFactory>()
                .CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using var contextDisposal = context.ConfigureAwait(false);
            return await context
                .Metadatas.AsNoTracking()
                .SingleAsync(m => m.Id == id)
                .ConfigureAwait(false);
        }

        public async Task Delete(long id)
        {
            var context = await Services
                .GetRequiredService<IDataContextProviderFactory>()
                .CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using var contextDisposal = context.ConfigureAwait(false);
            var row = await context.Metadatas.SingleAsync(m => m.Id == id).ConfigureAwait(false);
            context.Metadatas.Remove(row);
            await context.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<bool> GroupExists(string name)
        {
            var context = await Services
                .GetRequiredService<IDataContextProviderFactory>()
                .CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using var contextDisposal = context.ConfigureAwait(false);
            return await context
                .ManifestGroups.AsNoTracking()
                .AnyAsync(g => g.Name == name)
                .ConfigureAwait(false);
        }

        public async Task DeleteGroup(string name)
        {
            var context = await Services
                .GetRequiredService<IDataContextProviderFactory>()
                .CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using var contextDisposal = context.ConfigureAwait(false);
            context.ManifestGroups.RemoveRange(
                await context
                    .ManifestGroups.Where(g => g.Name == name)
                    .ToListAsync()
                    .ConfigureAwait(false)
            );
            await context.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync().ConfigureAwait(false);

            if (sqliteFile is null)
                return;

            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try
                {
                    File.Delete(sqliteFile + suffix);
                }
                catch (IOException)
                {
                    // Best effort: a lingering handle can hold the temp file.
                }
        }
    }

    /// <summary>Lets a test wait until the junctions it expects are all running.</summary>
    private static class Waits
    {
        private static int _expected;
        private static int _waiting;
        private static TaskCompletionSource _all = New();

        public static Task AllWaiting => _all.Task;

        public static void Reset(int expected)
        {
            _expected = expected;
            _waiting = 0;
            _all = New();
        }

        /// <summary>
        /// Says this junction is running, then completes only when the token is cancelled, by
        /// throwing.
        /// </summary>
        public static async Task UntilCancelled(CancellationToken token)
        {
            if (Interlocked.Increment(ref _waiting) == _expected)
                _all.TrySetResult();

            var cancelled = New();
            await using (
                (token.Register(() => cancelled.TrySetCanceled(token))).ConfigureAwait(false)
            )
                await cancelled.Task.ConfigureAwait(false);
        }

        private static TaskCompletionSource New() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed record BranchA;

    public sealed record BranchB;

    public class Start : EffectJunction<Unit, Unit>
    {
        public override Task<Unit> Run(Unit input) => Task.FromResult(Unit.Default);
    }

    public class WaitsToBeCancelled : EffectJunction<Unit, Unit>
    {
        public override async Task<Unit> Run(Unit input)
        {
            await Waits.UntilCancelled(CancellationToken).ConfigureAwait(false);
            return Unit.Default;
        }
    }

    public class WaitA : EffectJunction<Unit, BranchA>
    {
        public override async Task<BranchA> Run(Unit input)
        {
            await Waits.UntilCancelled(CancellationToken).ConfigureAwait(false);
            return new BranchA();
        }
    }

    public class WaitB : EffectJunction<Unit, BranchB>
    {
        public override async Task<BranchB> Run(Unit input)
        {
            await Waits.UntilCancelled(CancellationToken).ConfigureAwait(false);
            return new BranchB();
        }
    }

    /// <summary>Fails once its sibling is running, which cancels the sibling.</summary>
    public class BreaksOnceSiblingRuns : EffectJunction<Unit, BranchA>
    {
        public override async Task<BranchA> Run(Unit input)
        {
            await Waits
                .AllWaiting.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None)
                .ConfigureAwait(false);
            throw new InvalidOperationException("a broke");
        }
    }

    public class Join : EffectJunction<(BranchA, BranchB), Unit>
    {
        public override Task<Unit> Run((BranchA, BranchB) input) => Task.FromResult(Unit.Default);
    }

    public interface ICancelledTrain : IServiceTrain<Unit, Unit>;

    public class CancelledTrain : ServiceTrain<Unit, Unit>, ICancelledTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<WaitsToBeCancelled>().Resolve();
    }

    public interface ICancelledParallelTrain : IServiceTrain<Unit, Unit>;

    public class CancelledParallelTrain : ServiceTrain<Unit, Unit>, ICancelledParallelTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<Start>()
                .Parallel(p =>
                    p.Branch("a", b => b.Chain<WaitA>()).Branch("b", b => b.Chain<WaitB>())
                )
                .Chain<Join>()
                .Resolve();
    }

    public interface ICancelSiblingsTrain : IServiceTrain<Unit, Unit>;

    public class CancelSiblingsTrain : ServiceTrain<Unit, Unit>, ICancelSiblingsTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<Start>()
                .Parallel(p =>
                    p.Branch("a", b => b.Chain<BreaksOnceSiblingRuns>())
                        .Branch("b", b => b.Chain<WaitB>())
                )
                .Chain<Join>()
                .Resolve();
    }

    /// <summary>
    /// Tracks a manifest group through the run's effect runner in its first junction and throws in
    /// its second. Each junction reads its own progress column from the store first, and the second
    /// also looks for the group.
    /// </summary>
    public interface ITracksThenFailsTrain : IServiceTrain<Unit, Unit>;

    public class TracksThenFailsTrain : ServiceTrain<Unit, Unit>, ITracksThenFailsTrain
    {
        public static TracksThenFailsTrain? Instance { get; set; }
        public static IDataContextProviderFactory? Factory { get; set; }
        public static string Group { get; set; } = "";
        public static readonly List<string?> Seen = [];
        public static bool? GroupSeenMidRun { get; set; }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<TracksARow>().Chain<FailsAfterTheRow>().Resolve();

        public static async Task ReadProgress()
        {
            var context = await Factory
                .CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using var contextDisposal = context.ConfigureAwait(false);
            Seen.Add(
                await context
                    .Metadatas.AsNoTracking()
                    .Where(m => m.Id == Instance.Metadata!.Id)
                    .Select(m => m.CurrentlyRunningJunction)
                    .SingleAsync(cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false)
            );
        }
    }

    public class TracksARow : EffectJunction<Unit, Unit>
    {
        public override async Task<Unit> Run(Unit input)
        {
            await TracksThenFailsTrain.ReadProgress().ConfigureAwait(false);
            await TracksThenFailsTrain
                .Instance.EffectRunner!.Track(
                    new ManifestGroup
                    {
                        Name = TracksThenFailsTrain.Group,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    }
                )
                .ConfigureAwait(false);
            return Unit.Default;
        }
    }

    public class FailsAfterTheRow : EffectJunction<Unit, Unit>
    {
        public override async Task<Unit> Run(Unit input)
        {
            await TracksThenFailsTrain.ReadProgress().ConfigureAwait(false);
            var context = await TracksThenFailsTrain
                .Factory.CreateDbContextAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await using var contextDisposal = context.ConfigureAwait(false);
            TracksThenFailsTrain.GroupSeenMidRun = await context
                .ManifestGroups.AsNoTracking()
                .AnyAsync(
                    g => g.Name == TracksThenFailsTrain.Group,
                    cancellationToken: CancellationToken.None
                )
                .ConfigureAwait(false);
            throw new InvalidOperationException("fails after the row was tracked");
        }
    }

    private sealed class SaveCounter : IEffectProviderFactory
    {
        private int _saves;

        public int Saves => _saves;

        public IEffectProvider Create() => new Provider(this);

        private sealed class Provider(SaveCounter counter) : IEffectProvider
        {
            public Task SaveChanges(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref counter._saves);
                return Task.CompletedTask;
            }

            public Task Track(IModel model) => Task.CompletedTask;

            public Task Update(IModel model) => Task.CompletedTask;

            public void Dispose() { }
        }
    }

    public interface IThreeJunctionTrain : IServiceTrain<Unit, Unit>;

    public class ThreeJunctionTrain : ServiceTrain<Unit, Unit>, IThreeJunctionTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<Step>().Chain<Step>().Chain<Step>().Resolve();

        public class Step : EffectJunction<Unit, Unit>
        {
            public override Task<Unit> Run(Unit input) => Task.FromResult(Unit.Default);
        }
    }

    public interface IWatchedTrain : IServiceTrain<Unit, Unit>;

    public class WatchedTrain : ServiceTrain<Unit, Unit>, IWatchedTrain
    {
        public static string? Seen { get; set; }
        public static IDataContextProviderFactory? Factory { get; set; }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ReadsItsOwnProgress>().Resolve();

        public class ReadsItsOwnProgress : EffectJunction<Unit, Unit>
        {
            public override async Task<Unit> Run(Unit input)
            {
                var context = (IDataContext)Factory.Create();
                await using var contextDisposal = context.ConfigureAwait(false);
                Seen = await context
                    .Metadatas.AsNoTracking()
                    .Where(m =>
                        m.Name == typeof(IWatchedTrain).FullName
                        && m.TrainState == TrainState.InProgress
                    )
                    .Select(m => m.CurrentlyRunningJunction)
                    .SingleAsync(cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
                return Unit.Default;
            }
        }
    }
}
