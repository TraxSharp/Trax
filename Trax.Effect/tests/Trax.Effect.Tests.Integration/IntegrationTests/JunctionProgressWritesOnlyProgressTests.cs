using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// Junction progress writes the run's two progress columns and nothing else. It used to save
/// through the run's effect runner, which committed everything the run had tracked so far after
/// every junction and saved every other effect provider each time; a run's own writes now commit
/// once, when it finishes.
///
/// <para>Enforces <c>docs/adr/0021-a-runs-tracked-writes-commit-once-when-it-finishes.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0021-a-runs-tracked-writes-commit-once-when-it-finishes.md")]
public class JunctionProgressWritesOnlyProgressTests
{
    private const string Adr =
        "docs/adr/0021-a-runs-tracked-writes-commit-once-when-it-finishes.md";

    [Test]
    public async Task Progress_does_not_save_the_runs_other_effects_after_each_junction()
    {
        var counter = new SaveCounter();
        await using var provider = Build(counter, progress: true);
        using var scope = provider.CreateScope();
        var train = (ThreeJunctionTrain)
            scope.ServiceProvider.GetRequiredService<IThreeJunctionTrain>();

        await train.Run(Unit.Default);

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
        await using var provider = Build(new SaveCounter(), progress: true);
        using var scope = provider.CreateScope();
        var train = (WatchedTrain)scope.ServiceProvider.GetRequiredService<IWatchedTrain>();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        WatchedTrain.Seen = null;
        WatchedTrain.Factory = factory;

        await train.Run(Unit.Default);

        WatchedTrain
            .Seen.Should()
            .Be(
                nameof(WatchedTrain.ReadsItsOwnProgress),
                "the column is written before the junction runs"
            );

        using var context = (IDataContext)factory.Create();
        var row = await context
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.Id == train.Metadata!.Id);
        row.CurrentlyRunningJunction.Should().BeNull("the run's final write clears it");
        row.JunctionStartedAt.Should().BeNull();
    }

    private static async Task<int> SavesWithoutProgress()
    {
        var counter = new SaveCounter();
        await using var provider = Build(counter, progress: false);
        using var scope = provider.CreateScope();
        var train = (ThreeJunctionTrain)
            scope.ServiceProvider.GetRequiredService<IThreeJunctionTrain>();

        await train.Run(Unit.Default);

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
        public static string? Seen;
        public static IDataContextProviderFactory Factory = null!;

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ReadsItsOwnProgress>().Resolve();

        public class ReadsItsOwnProgress : EffectJunction<Unit, Unit>
        {
            public override async Task<Unit> Run(Unit input)
            {
                using var context = (IDataContext)Factory.Create();
                Seen = await context
                    .Metadatas.AsNoTracking()
                    .Where(m =>
                        m.Name == typeof(IWatchedTrain).FullName
                        && m.TrainState == TrainState.InProgress
                    )
                    .Select(m => m.CurrentlyRunningJunction)
                    .SingleAsync();
                return Unit.Default;
            }
        }
    }
}
