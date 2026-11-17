using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Exceptions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// A pre-created <c>Pending</c> row is started by exactly one run on SQLite too. The claim is a
/// conditional <c>ExecuteUpdateAsync</c> over <c>train_state</c>, which SQLite stores as an
/// integer rather than a Postgres enum label, so the Postgres suite does not cover it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SqlitePendingRunClaimTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.AddScopedTraxRoute<ICountingTrain, CountingTrain>().BuildServiceProvider();

    [SetUp]
    public void ResetCount() => CountingTrain.BodyRuns = 0;

    [Test]
    public async Task Two_concurrent_starts_of_one_Pending_row_run_the_body_once()
    {
        var id = await CreatePendingRow();
        var copyA = await ReadRow(id);
        var copyB = await ReadRow(id);
        var scopeA = Scope.ServiceProvider.CreateAsyncScope();
        await using var scopeADisposal = scopeA.ConfigureAwait(false);
        var scopeB = Scope.ServiceProvider.CreateAsyncScope();
        await using var scopeBDisposal = scopeB.ConfigureAwait(false);
        var trainA = (CountingTrain)scopeA.ServiceProvider.GetRequiredService<ICountingTrain>();
        var trainB = (CountingTrain)scopeB.ServiceProvider.GetRequiredService<ICountingTrain>();

        var outcomes = await Task.WhenAll(
            Settle(Task.Run(() => trainA.Run(Unit.Default, copyA))),
            Settle(Task.Run(() => trainB.Run(Unit.Default, copyB)))
        );

        CountingTrain.BodyRuns.Should().Be(1, "only one delivery may run the train");
        outcomes
            .Should()
            .ContainSingle(o => o is TrainAlreadyStartedException, "the other delivery is refused")
            .And.ContainSingle(o => o == null);
        (await ReadRow(id)).TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task A_start_from_a_stale_Pending_copy_of_a_finished_row_is_refused()
    {
        var id = await CreatePendingRow();
        var staleCopy = await ReadRow(id);

        var first = Scope.ServiceProvider.CreateAsyncScope();
        await using (first.ConfigureAwait(false))
            await ((CountingTrain)first.ServiceProvider.GetRequiredService<ICountingTrain>()).Run(
                Unit.Default,
                await ReadRow(id)
            );

        var second = Scope.ServiceProvider.CreateAsyncScope();
        await using var secondDisposal = second.ConfigureAwait(false);
        var train = (CountingTrain)second.ServiceProvider.GetRequiredService<ICountingTrain>();
        var act = async () => await train.Run(Unit.Default, staleCopy).ConfigureAwait(false);

        await act.Should().ThrowAsync<TrainAlreadyStartedException>();
        CountingTrain.BodyRuns.Should().Be(1);
        (await ReadRow(id)).TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task A_supplied_row_that_was_never_saved_is_the_runs_own()
    {
        var scope = Scope.ServiceProvider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = (CountingTrain)scope.ServiceProvider.GetRequiredService<ICountingTrain>();
        var unsaved = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ICountingTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        await train.Run(Unit.Default, unsaved);

        CountingTrain.BodyRuns.Should().Be(1);
        (await ReadRow(unsaved.Id)).TrainState.Should().Be(TrainState.Completed);
    }

    private static async Task<Exception?> Settle(Task run)
    {
        try
        {
            await run.ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<long> CreatePendingRow()
    {
        var context = (IDataContext)
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>().Create();
        await using var contextDisposal = context.ConfigureAwait(false);
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ICountingTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = Unit.Default,
            }
        );
        await context.Track(metadata).ConfigureAwait(false);
        await context.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        return metadata.Id;
    }

    private async Task<Metadata> ReadRow(long id)
    {
        var context = (IDataContext)
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>().Create();
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context
            .Metadatas.AsNoTracking()
            .SingleAsync(m => m.Id == id)
            .ConfigureAwait(false);
    }

    public interface ICountingTrain : IServiceTrain<Unit, Unit>;

    public class CountingTrain : ServiceTrain<Unit, Unit>, ICountingTrain
    {
        private static int _bodyRuns;

        public static int BodyRuns
        {
            get => Volatile.Read(ref _bodyRuns);
            set => Volatile.Write(ref _bodyRuns, value);
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<CountsJunction>().Resolve();

        public class CountsJunction : EffectJunction<Unit, Unit>
        {
            public override Task<Unit> Run(Unit input)
            {
                Interlocked.Increment(ref _bodyRuns);
                return Task.FromResult(input);
            }
        }
    }
}
