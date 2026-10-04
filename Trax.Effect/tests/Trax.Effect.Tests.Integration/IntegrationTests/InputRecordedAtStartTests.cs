using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The row a run writes when it starts carries its input, so a process that dies mid-run (a
/// Lambda timeout, an OOM kill, a deploy) still leaves a copy of what the run was given. The data
/// provider is registered before the parameter effect here, the order in which the data provider
/// flushes before the parameter effect serializes.
/// </summary>
[TestFixture]
[NonParallelizable]
public class InputRecordedAtStartTests
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
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .UsePostgres(connectionString)
                    .SaveTrainParameters(configure: cfg => cfg.ExcludeInput<IExcludedTrain>())
            )
        );
        services
            .AddScopedTraxRoute<IObservedTrain, ObservedTrain>()
            .AddScopedTraxRoute<IExcludedTrain, ExcludedTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task The_row_is_in_progress_with_its_input_while_the_body_runs()
    {
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IObservedTrain>();

        await train.Run(new StartInput(42));

        var seen = _seen!;
        seen.State.Should().Be(TrainState.InProgress);
        seen.Input.Should()
            .NotBeNull(
                "a run killed before its outcome is written must still have a copy of its input"
            );
        seen.Input.Should().Contain("42");
    }

    [Test]
    public async Task An_excluded_input_is_not_written_at_start_either()
    {
        using var scope = _provider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IExcludedTrain>();

        await train.Run(new StartInput(7));

        var seen = _seen!;
        seen.State.Should().Be(TrainState.InProgress);
        seen.Input.Should().BeNull("the host excluded this train's input");
    }

    public record StartInput(int Value);

    public record SeenRow(TrainState State, string? Input);

    private static SeenRow? _seen;

    [SetUp]
    public void ForgetLastRow() => _seen = null;

    /// <summary>Reads the run's own row through a separate context, as another process would.</summary>
    public class ReadOwnRowJunction(IDataContextProviderFactory factory)
        : EffectJunction<StartInput, Unit>
    {
        public override async Task<Unit> Run(StartInput input)
        {
            var id = Metadata!.TrainMetadataId;
            using var context = (IDataContext)factory.Create();
            var row = await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == id);
            _seen = new SeenRow(row.TrainState, row.Input);

            return Unit.Default;
        }
    }

    public interface IObservedTrain : IServiceTrain<StartInput, Unit>;

    public class ObservedTrain : ServiceTrain<StartInput, Unit>, IObservedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ReadOwnRowJunction>().Resolve();
    }

    public interface IExcludedTrain : IServiceTrain<StartInput, Unit>;

    public class ExcludedTrain : ServiceTrain<StartInput, Unit>, IExcludedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ReadOwnRowJunction>().Resolve();
    }
}
