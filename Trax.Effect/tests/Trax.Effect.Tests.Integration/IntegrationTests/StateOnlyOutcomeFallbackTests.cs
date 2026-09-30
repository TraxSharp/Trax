using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Exceptions;
using Trax.Effect.Extensions;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// When the store refuses a run's row because of what it carries (its input, its output, or the
/// failure's message and stack trace), the run still records its state and end time without that
/// content, rather than staying <c>InProgress</c> until the stale-run reaper fails it. Only the
/// store's own content refusal does this: any other provider's failure propagates, and a row the
/// store already saved in full is never written over with placeholders.
///
/// <para>Enforces <c>docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md")]
public class StateOnlyOutcomeFallbackTests
{
    private const string Unwritable = "unwritable-content";

    [Test]
    public async Task An_output_the_store_refuses_still_records_Completed()
    {
        var store = new ContentRefusingStore();
        await using var provider = Build(effects => effects.AddEffect(store.Factory));
        using var scope = provider.CreateScope();
        var train = (UnwritableOutputTrain)
            scope.ServiceProvider.GetRequiredService<IUnwritableOutputTrain>();

        var output = await train.Run(Unit.Default);

        output.Should().Be(Unwritable, "the work happened, and its caller gets its output");
        var row = store.Saved[train.Metadata!];
        row.TrainState.Should()
            .Be(
                TrainState.Completed,
                "0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md records the state "
                    + "without the content the store refused"
            );
        row.EndTime.Should().NotBeNull();
        row.Output.Should().NotBeNull().And.NotContain(Unwritable);
    }

    [Test]
    public async Task A_failure_whose_message_the_store_refuses_still_records_Failed()
    {
        var store = new ContentRefusingStore();
        await using var provider = Build(effects => effects.AddEffect(store.Factory));
        using var scope = provider.CreateScope();
        var train = (UnwritableFailureTrain)
            scope.ServiceProvider.GetRequiredService<IUnwritableFailureTrain>();

        var act = async () => await train.Run(Unit.Default);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            $"*{Unwritable}*",
            "the train's own failure still reaches its caller"
        );

        var row = store.Saved[train.Metadata!];
        row.TrainState.Should().Be(TrainState.Failed);
        row.EndTime.Should().NotBeNull();
        row.FailureException.Should().Be(nameof(InvalidOperationException));
        row.FailureReason.Should().NotBeNull().And.NotContain(Unwritable);
    }

    [Test]
    public async Task An_input_the_store_refuses_still_records_the_run()
    {
        var store = new ContentRefusingStore();
        await using var provider = Build(effects => effects.AddEffect(store.Factory));
        using var scope = provider.CreateScope();
        var train = (EchoTrain)scope.ServiceProvider.GetRequiredService<IEchoTrain>();

        var output = await train.Run(Unwritable);

        output.Should().Be(Unwritable);
        var row = store.Saved[train.Metadata!];
        row.TrainState.Should().Be(TrainState.Completed);
        row.EndTime.Should().NotBeNull();
        row.Input.Should().NotBeNull().And.NotContain(Unwritable);
    }

    [Test]
    public async Task A_throwing_effect_after_the_store_does_not_erase_the_saved_output()
    {
        await using var provider = Build(effects =>
            effects.AddEffect(new ThrowsOnOutcomeEffectFactory())
        );
        using var scope = provider.CreateScope();
        var train = (UnwritableOutputTrain)
            scope.ServiceProvider.GetRequiredService<IUnwritableOutputTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should()
            .ThrowAsync<TimeoutException>(
                "a provider that failed for any reason but the store's content refusal propagates"
            );

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Completed);
        row.Output.Should()
            .Contain(
                Unwritable,
                "the store saved the full row, and nothing may write placeholders over it"
            );
    }

    [Test]
    public async Task A_throwing_effect_after_the_store_does_not_erase_the_saved_failure()
    {
        await using var provider = Build(effects =>
            effects.AddEffect(new ThrowsOnOutcomeEffectFactory())
        );
        using var scope = provider.CreateScope();
        var train = (UnwritableFailureTrain)
            scope.ServiceProvider.GetRequiredService<IUnwritableFailureTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var row = await PersistedRow(scope, train.Metadata!.Id);
        row.TrainState.Should().Be(TrainState.Failed);
        row.FailureReason.Should().Contain(Unwritable);
        row.StackTrace.Should().NotBeNull();
    }

    [Test]
    public async Task Hooks_see_the_real_output_after_a_fallback()
    {
        var store = new ContentRefusingStore();
        var hook = new RecordingHook();
        await using var provider = Build(
            effects => effects.AddEffect(store.Factory),
            services => services.AddSingleton<ITrainLifecycleHookFactory>(hook)
        );
        using var scope = provider.CreateScope();
        var train = (UnwritableOutputTrain)
            scope.ServiceProvider.GetRequiredService<IUnwritableOutputTrain>();

        await train.Run(Unit.Default);

        store.Saved[train.Metadata!].Output.Should().NotContain(Unwritable);
        hook.Output.Should()
            .Contain(Unwritable, "the store's refusal limits what is stored, not what happened");
    }

    [Test]
    public async Task Failure_hooks_see_the_real_reason_after_a_fallback()
    {
        var store = new ContentRefusingStore();
        var hook = new RecordingHook();
        await using var provider = Build(
            effects => effects.AddEffect(store.Factory),
            services => services.AddSingleton<ITrainLifecycleHookFactory>(hook)
        );
        using var scope = provider.CreateScope();
        var train = (UnwritableFailureTrain)
            scope.ServiceProvider.GetRequiredService<IUnwritableFailureTrain>();

        var act = async () => await train.Run(Unit.Default);
        await act.Should().ThrowAsync<InvalidOperationException>();

        store.Saved[train.Metadata!].FailureReason.Should().NotContain(Unwritable);
        hook.FailureReason.Should().Contain(Unwritable);
    }

    private static ServiceProvider Build(
        Func<TraxEffectBuilder, TraxEffectBuilder> configure,
        Action<IServiceCollection>? arrange = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => configure(effects.UseInMemory()).SaveTrainParameters())
        );
        services
            .AddScopedTraxRoute<IUnwritableOutputTrain, UnwritableOutputTrain>()
            .AddScopedTraxRoute<IUnwritableFailureTrain, UnwritableFailureTrain>()
            .AddScopedTraxRoute<IEchoTrain, EchoTrain>();
        arrange?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static async Task<Metadata> PersistedRow(IServiceScope scope, long id)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    /// <summary>
    /// A store that refuses a row because of its content, as Postgres refuses a character it
    /// cannot store, and says so the way a store does: with
    /// <see cref="StoreRefusedContentException"/>. It keeps a copy of each row it accepted.
    /// </summary>
    private sealed class ContentRefusingStore
    {
        public Dictionary<Metadata, SavedRow> Saved { get; } = [];

        public IEffectProviderFactory Factory => new StoreFactory(this);

        private sealed class StoreFactory(ContentRefusingStore store) : IEffectProviderFactory
        {
            public IEffectProvider Create() => new Provider(store);
        }

        private sealed class Provider(ContentRefusingStore store) : IEffectProvider
        {
            private readonly List<Metadata> _tracked = [];

            public Task SaveChanges(CancellationToken cancellationToken)
            {
                foreach (var metadata in _tracked)
                {
                    if (
                        new[]
                        {
                            metadata.Input,
                            metadata.Output,
                            metadata.FailureReason,
                            metadata.StackTrace,
                        }.Any(value => value?.Contains(Unwritable) ?? false)
                    )
                        throw new StoreRefusedContentException(
                            new InvalidDataException("the store refused the row's content")
                        );

                    store.Saved[metadata] = new SavedRow(
                        metadata.TrainState,
                        metadata.EndTime,
                        metadata.Input,
                        metadata.Output,
                        metadata.FailureException,
                        metadata.FailureReason,
                        metadata.StackTrace
                    );
                }

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
    }

    private sealed record SavedRow(
        TrainState TrainState,
        DateTime? EndTime,
        string? Input,
        string? Output,
        string? FailureException,
        string? FailureReason,
        string? StackTrace
    );

    /// <summary>
    /// An effect that is not the store and fails for a reason that has nothing to do with the
    /// row's content, once the run is terminal.
    /// </summary>
    private sealed class ThrowsOnOutcomeEffect : IEffectProvider
    {
        private readonly List<Metadata> _tracked = [];

        public Task SaveChanges(CancellationToken cancellationToken) =>
            _tracked.Any(m => m.TrainState is TrainState.Completed or TrainState.Failed)
                ? throw new TimeoutException("the effect's endpoint timed out")
                : Task.CompletedTask;

        public Task Track(IModel model)
        {
            if (model is Metadata metadata)
                _tracked.Add(metadata);
            return Task.CompletedTask;
        }

        public Task Update(IModel model) => Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class ThrowsOnOutcomeEffectFactory : IEffectProviderFactory
    {
        public IEffectProvider Create() => new ThrowsOnOutcomeEffect();
    }

    private sealed class RecordingHook : ITrainLifecycleHook, ITrainLifecycleHookFactory
    {
        public string? Output { get; private set; }
        public string? FailureReason { get; private set; }

        public Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            Output = metadata.Output;
            return Task.CompletedTask;
        }

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
        {
            FailureReason = metadata.FailureReason;
            return Task.CompletedTask;
        }

        public ITrainLifecycleHook Create() => this;
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

    public interface IEchoTrain : IServiceTrain<string, string>;

    public class EchoTrain : ServiceTrain<string, string>, IEchoTrain
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Task.FromResult<Either<Exception, string>>(TrainInput);
    }
}
