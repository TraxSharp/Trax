using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// When the outcome cannot be written because of what the row carries (its output, or the
/// failure's message and stack trace), the run still records its state and end time without that
/// content, rather than staying <c>InProgress</c> until the stale-run reaper fails it.
///
/// <para>Enforces <c>docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md")]
public class StateOnlyOutcomeFallbackTests
{
    private const string Unwritable = "unwritable-content";
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .UseInMemory()
                    .SaveTrainParameters()
                    .AddEffect(new ContentRefusingEffectFactory())
            )
        );
        services
            .AddScopedTraxRoute<IUnwritableOutputTrain, UnwritableOutputTrain>()
            .AddScopedTraxRoute<IUnwritableFailureTrain, UnwritableFailureTrain>();
        _provider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [Test]
    public async Task An_output_that_cannot_be_written_still_records_Completed()
    {
        using var scope = _provider.CreateScope();
        var train = (UnwritableOutputTrain)
            scope.ServiceProvider.GetRequiredService<IUnwritableOutputTrain>();

        var output = await train.Run(Unit.Default);

        output.Should().Be(Unwritable, "the work happened, and its caller gets its output");
        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should()
            .Be(
                TrainState.Completed,
                "0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md records the state "
                    + "without the content the store refused"
            );
        row.EndTime.Should().NotBeNull();
        row.Output.Should().NotContain(Unwritable);
    }

    [Test]
    public async Task A_failure_whose_message_cannot_be_written_still_records_Failed()
    {
        using var scope = _provider.CreateScope();
        var train = (UnwritableFailureTrain)
            scope.ServiceProvider.GetRequiredService<IUnwritableFailureTrain>();

        var act = async () => await train.Run(Unit.Default);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            $"*{Unwritable}*",
            "the train's own failure still reaches its caller"
        );

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Failed);
        row.EndTime.Should().NotBeNull();
        row.FailureException.Should().Be(nameof(InvalidOperationException));
        row.FailureReason.Should().NotContain(Unwritable);
    }

    private static async Task<Metadata> PersistedRow(IServiceScope scope, long id)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    /// <summary>
    /// Stands in for a store that refuses a row because of its content, as Postgres refuses a
    /// character it cannot store: it fails every save while a tracked run carries the marker.
    /// </summary>
    private sealed class ContentRefusingEffect : IEffectProvider
    {
        private readonly List<Metadata> _tracked = [];

        public Task SaveChanges(CancellationToken cancellationToken)
        {
            foreach (var metadata in _tracked)
                if (
                    (metadata.Output?.Contains(Unwritable) ?? false)
                    || (metadata.FailureReason?.Contains(Unwritable) ?? false)
                    || (metadata.StackTrace?.Contains(Unwritable) ?? false)
                )
                    throw new InvalidDataException("the store refused the row's content");

            return Task.CompletedTask;
        }

        public Task Track(IModel model)
        {
            if (model is Metadata metadata)
                _tracked.Add(metadata);
            return Task.CompletedTask;
        }

        public Task Update(IModel model) => Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class ContentRefusingEffectFactory : IEffectProviderFactory
    {
        public IEffectProvider Create() => new ContentRefusingEffect();
    }

    public interface IUnwritableOutputTrain : IServiceTrain<Unit, string>;

    public class UnwritableOutputTrain : ServiceTrain<Unit, string>, IUnwritableOutputTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<ReturnsUnwritableJunction>().Resolve();

        public class ReturnsUnwritableJunction : EffectJunction<Unit, string>
        {
            public override Task<string> Run(Unit input) => Task.FromResult(Unwritable);
        }
    }

    public interface IUnwritableFailureTrain : IServiceTrain<Unit, Unit>;

    public class UnwritableFailureTrain : ServiceTrain<Unit, Unit>, IUnwritableFailureTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ThrowsUnwritableJunction>().Resolve();

        public class ThrowsUnwritableJunction : EffectJunction<Unit, Unit>
        {
            public override Task<Unit> Run(Unit input) =>
                throw new InvalidOperationException($"failed on {Unwritable}");
        }
    }
}
