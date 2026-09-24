using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// What a train reports when the write that records its outcome fails.
///
/// <para>A completed run's save error propagates as it is, and the run is not rewritten as
/// <c>Failed</c>, because the work happened; the row stays <c>InProgress</c> for the stale-run
/// reaper. A failed run's recording error is logged, and the train's own failure propagates
/// with its failure hooks, so the caller learns why the train failed rather than why the
/// bookkeeping did.</para>
///
/// <para>Enforces <c>docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md")]
public class OutcomeSaveFailureTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services)
    {
        // First in line, so the in-memory provider never gets to write the terminal state and the
        // persisted row shows what a lone failing provider would leave behind.
        services.Insert(
            0,
            ServiceDescriptor.Singleton<IEffectProviderFactory, FailingOutcomeSaveFactory>()
        );

        return services
            .AddScopedTraxRoute<ICompletingTrain, CompletingTrain>()
            .AddScopedTraxRoute<IFailingTrain, FailingTrain>()
            .BuildServiceProvider();
    }

    [SetUp]
    public void ResetProbe() => Probe.Reset();

    [Test]
    public async Task Run_WhenSavingACompletedOutcomeThrows_PropagatesTheSaveErrorWithoutFailingTheRun()
    {
        Probe.FailSavingState = TrainState.Completed;
        var train = (CompletingTrain)Scope.ServiceProvider.GetRequiredService<ICompletingTrain>();

        var run = async () => await train.Run(Unit.Default);

        await run.Should().ThrowExactlyAsync<OutcomeSaveException>();
        train
            .Metadata!.TrainState.Should()
            .NotBe(
                TrainState.Failed,
                "0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md says the work "
                    + "happened, so recording that it failed would be false"
            );
        Probe.OnFailedCalls.Should().Be(0);
        Probe.OnCompletedCalls.Should().Be(0, "the outcome was never recorded");

        var row = await PersistedRow(typeof(ICompletingTrain).FullName!);
        row.TrainState.Should()
            .Be(TrainState.InProgress, "the row is left for the stale-run reaper to resolve");
    }

    [Test]
    public async Task Run_WhenSavingAFailedOutcomeThrows_PropagatesTheOriginalFailureAndFiresOnFailedOnce()
    {
        Probe.FailSavingState = TrainState.Failed;
        var train = (FailingTrain)Scope.ServiceProvider.GetRequiredService<IFailingTrain>();

        var run = async () => await train.Run(Unit.Default);

        (await run.Should().ThrowAsync<Exception>())
            .Which.Should()
            .BeSameAs(
                FailingTrain.Failure,
                "0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md says the caller "
                    + "learns why the train failed, not why the bookkeeping did"
            );
        Probe.OnFailedCalls.Should().Be(1);
        Probe.OnFailedException.Should().BeSameAs(FailingTrain.Failure);
    }

    private async Task<Metadata> PersistedRow(string trainName)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Name == trainName);
    }

    private static class Probe
    {
        public static TrainState? FailSavingState { get; set; }

        public static int OnFailedCalls { get; set; }

        public static Exception? OnFailedException { get; set; }

        public static int OnCompletedCalls { get; set; }

        public static void Reset()
        {
            FailSavingState = null;
            OnFailedCalls = 0;
            OnFailedException = null;
            OnCompletedCalls = 0;
        }
    }

    private class OutcomeSaveException() : Exception("The outcome could not be saved.");

    /// <summary>Throws from SaveChanges once the tracked run has reached the probed state.</summary>
    private class FailingOutcomeSaveProvider : IEffectProvider
    {
        private readonly List<Metadata> _tracked = [];

        public Task SaveChanges(CancellationToken cancellationToken) =>
            _tracked.Any(m => m.TrainState == Probe.FailSavingState)
                ? throw new OutcomeSaveException()
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

    private class FailingOutcomeSaveFactory : IEffectProviderFactory
    {
        public IEffectProvider Create() => new FailingOutcomeSaveProvider();
    }

    private class CompletingTrain : ServiceTrain<Unit, Unit>, ICompletingTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);

        protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
        {
            Probe.OnCompletedCalls++;
            return Task.CompletedTask;
        }

        protected override Task OnFailed(
            Metadata metadata,
            Exception exception,
            CancellationToken ct
        )
        {
            Probe.OnFailedCalls++;
            return Task.CompletedTask;
        }
    }

    private class FailingTrain : ServiceTrain<Unit, Unit>, IFailingTrain
    {
        public static readonly InvalidOperationException Failure = new("The train's own failure.");

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Failure);

        protected override Task OnFailed(
            Metadata metadata,
            Exception exception,
            CancellationToken ct
        )
        {
            Probe.OnFailedCalls++;
            Probe.OnFailedException = exception;
            return Task.CompletedTask;
        }
    }

    private interface ICompletingTrain : IServiceTrain<Unit, Unit> { }

    private interface IFailingTrain : IServiceTrain<Unit, Unit> { }
}
