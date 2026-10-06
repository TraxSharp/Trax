using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Exceptions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A pre-created <c>Pending</c> row is started by exactly one run. Two deliveries of the same job
/// (an at-least-once queue, a retried HTTP dispatch, a local re-claim) each hold a copy of the row
/// that says <c>Pending</c>; only one of them may move it to <c>InProgress</c> and run the body.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PendingRunClaimTests
{
    private ServiceProvider _provider = null!;

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
        services.AddTrax(trax => trax.AddEffects(effects => effects.UsePostgres(connectionString)));
        services.AddScopedTraxRoute<ICountingTrain, CountingTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [SetUp]
    public void ResetCount() => CountingTrain.BodyRuns = 0;

    [Test]
    public async Task Two_concurrent_starts_of_one_Pending_row_run_the_body_once()
    {
        var id = await CreatePendingRow();

        // Both deliveries read the row before either starts, so each holds a copy that says Pending.
        var copyA = await ReadRow(id);
        var copyB = await ReadRow(id);
        using var scopeA = _provider.CreateScope();
        using var scopeB = _provider.CreateScope();
        var trainA = (CountingTrain)scopeA.ServiceProvider.GetRequiredService<ICountingTrain>();
        var trainB = (CountingTrain)scopeB.ServiceProvider.GetRequiredService<ICountingTrain>();

        var runA = Task.Run(() => trainA.Run(Unit.Default, copyA));
        var runB = Task.Run(() => trainB.Run(Unit.Default, copyB));

        var outcomes = await Task.WhenAll(Settle(runA), Settle(runB));

        CountingTrain.BodyRuns.Should().Be(1, "only one delivery may run the train");
        outcomes.Count(o => o is null).Should().Be(1);
        outcomes
            .Single(o => o is not null)
            .Should()
            .BeOfType<TrainAlreadyStartedException>("the other delivery is refused");

        var row = await ReadRow(id);
        row.TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task A_start_from_a_stale_Pending_copy_of_a_finished_row_is_refused()
    {
        var id = await CreatePendingRow();

        using var first = _provider.CreateScope();
        using var second = _provider.CreateScope();
        var staleCopy = await ReadRow(id);

        await StartFromRow(first, id);
        var train = (CountingTrain)second.ServiceProvider.GetRequiredService<ICountingTrain>();
        var act = async () => await train.Run(Unit.Default, staleCopy);

        await act.Should().ThrowAsync<TrainAlreadyStartedException>();
        CountingTrain.BodyRuns.Should().Be(1);
        (await ReadRow(id)).TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task A_supplied_row_that_was_never_saved_is_the_runs_own()
    {
        using var scope = _provider.CreateScope();
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

    private async Task StartFromRow(IServiceScope scope, long id)
    {
        var metadata = await ReadRow(id);
        var train = (CountingTrain)scope.ServiceProvider.GetRequiredService<ICountingTrain>();
        await train.Run(Unit.Default, metadata);
    }

    private static async Task<Exception?> Settle(Task run)
    {
        try
        {
            await run;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<long> CreatePendingRow()
    {
        using var context = (IDataContext)
            _provider.GetRequiredService<IDataContextProviderFactory>().Create();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ICountingTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = Unit.Default,
            }
        );
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        return metadata.Id;
    }

    private async Task<Metadata> ReadRow(long id)
    {
        using var context = (IDataContext)
            _provider.GetRequiredService<IDataContextProviderFactory>().Create();

        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == id);
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
