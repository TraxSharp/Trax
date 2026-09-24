using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// A train records how it ended even when the caller's token is what ended it.
///
/// <para>The terminal write is the audit record of the work, not part of the work, so it does
/// not run on the caller's token. Were it to, the one case the record exists for — the caller
/// cancelling — is the one case it could never be written, leaving the row at
/// <c>InProgress</c> for a stale-in-progress reaper to later rewrite to <c>Failed</c>
/// regardless of what the train actually did.</para>
///
/// <para>Enforces <c>docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md")]
public class CancelledOutcomePersistenceTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddScopedTraxRoute<IParkedTrain, ParkedTrain>()
            .AddScopedTraxRoute<IUninterruptibleTrain, UninterruptibleTrain>()
            .BuildServiceProvider();

    [SetUp]
    public void ResetProbe() => Probe.Reset();

    [Test]
    public async Task Run_CancelledMidFlight_PersistsCancelledAndAnEndTime()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IParkedTrain>();
        using var cts = new CancellationTokenSource();

        var runTask = train.Run(Unit.Default, cts.Token);
        try
        {
            await BodyStarted(runTask);
            await cts.CancelAsync();

            var awaiting = async () => await runTask;
            await awaiting.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            // Unpark the body if the test failed before cancelling it, so no run outlives it.
            await cts.CancelAsync();
        }

        var row = await PersistedRow(typeof(IParkedTrain).FullName!);

        row.TrainState.Should()
            .Be(
                TrainState.Cancelled,
                "0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md requires the "
                    + "outcome to be written on a token the caller cannot "
                    + "cancel; an InProgress row here means the terminal SaveChanges took the "
                    + "caller's token again"
            );
        row.EndTime.Should().NotBeNull();
    }

    [Test]
    public async Task Run_CancelledWhileTheWorkCompletesAnyway_PersistsCompleted()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IUninterruptibleTrain>();
        using var cts = new CancellationTokenSource();

        var runTask = train.Run(Unit.Default, cts.Token);
        try
        {
            await BodyStarted(runTask);
            await cts.CancelAsync();

            // The downstream system answers after the caller has already given up.
            Probe.Downstream.SetResult();
            await runTask;
        }
        finally
        {
            // Release the body if the test failed before answering it, so no run outlives it.
            Probe.Downstream.TrySetResult();
        }

        var row = await PersistedRow(typeof(IUninterruptibleTrain).FullName!);

        row.TrainState.Should()
            .Be(
                TrainState.Completed,
                "0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md requires work "
                    + "that finished to be recorded as finished, whatever the "
                    + "caller's token says; an InProgress row here means the terminal SaveChanges "
                    + "took the caller's token again"
            );
        row.EndTime.Should().NotBeNull();
    }

    /// <summary>
    /// Waits for the train's body to start, bounded, and watching the run itself: a run that
    /// faults before its body starts never signals the probe, so waiting on the probe alone
    /// would hang rather than report the fault.
    /// </summary>
    private static async Task BodyStarted(Task runTask)
    {
        var first = await Task.WhenAny(Probe.Started.Task, runTask)
            .WaitAsync(TimeSpan.FromSeconds(15));

        if (first == runTask)
            // Surfaces the run's own exception, if it has one.
            await runTask;

        first
            .Should()
            .BeSameAs(
                Probe.Started.Task,
                "the run ended before its body started, so it never reached the point the test cancels at"
            );
    }

    private async Task<Models.Metadata.Metadata> PersistedRow(string trainName)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Name == trainName);
    }

    private static class Probe
    {
        public static TaskCompletionSource Started { get; private set; } = Fresh();

        public static TaskCompletionSource Downstream { get; private set; } = Fresh();

        public static void Reset()
        {
            Started = Fresh();
            Downstream = Fresh();
        }

        private static TaskCompletionSource Fresh() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Parks until the caller's token releases it, as a cancellable downstream call would.</summary>
    private class ParkedTrain : ServiceTrain<Unit, Unit>, IParkedTrain
    {
        protected override async Task<Either<Exception, Unit>> Junctions()
        {
            var parked = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            await using var registration = CancellationToken.Register(() =>
                parked.TrySetCanceled()
            );

            Probe.Started.TrySetResult();
            await parked.Task;

            return Resolve();
        }
    }

    /// <summary>Takes no token, as a downstream SDK without cancellation support would.</summary>
    private class UninterruptibleTrain : ServiceTrain<Unit, Unit>, IUninterruptibleTrain
    {
        protected override async Task<Either<Exception, Unit>> Junctions()
        {
            Probe.Started.TrySetResult();
            await Probe.Downstream.Task;

            return Resolve();
        }
    }

    private interface IParkedTrain : IServiceTrain<Unit, Unit> { }

    private interface IUninterruptibleTrain : IServiceTrain<Unit, Unit> { }
}
