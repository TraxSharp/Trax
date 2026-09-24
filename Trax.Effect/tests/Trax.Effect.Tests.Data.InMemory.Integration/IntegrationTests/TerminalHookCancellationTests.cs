using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// A terminal lifecycle event reaches its hooks even when the caller's token is what ended the run.
///
/// <para>The same reasoning as the terminal write in effect/0005, one step further out. A hook
/// reporting the outcome is not part of the work the caller cancelled, so handing it the caller's
/// token means the one event it exists to deliver is the one it can never deliver: the broadcaster
/// passes that token to its publish, the publish is cancelled, and LifecycleHookRunner swallows the
/// error by design, so the event is dropped with nothing to show for it.</para>
/// </summary>
public class TerminalHookCancellationTests : TestSetup
{
    private static readonly RecordingHook Hook = new();

    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services
            .AddSingleton<ITrainLifecycleHookFactory>(new RecordingHookFactory(Hook))
            .AddScopedTraxRoute<IParkedHookTrain, ParkedHookTrain>()
            .AddScopedTraxRoute<IUninterruptibleHookTrain, UninterruptibleHookTrain>()
            .BuildServiceProvider();

    [SetUp]
    public void ResetState()
    {
        Hook.Reset();
        Probe.Reset();
    }

    [Test]
    public async Task A_cancelled_run_still_delivers_its_cancelled_event()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IParkedHookTrain>();
        using var cts = new CancellationTokenSource();

        var runTask = train.Run(Unit.Default, cts.Token);
        try
        {
            await Task.WhenAny(Probe.Started.Task, runTask).WaitAsync(TimeSpan.FromSeconds(15));
            await cts.CancelAsync();

            var awaiting = async () => await runTask;
            await awaiting.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            await cts.CancelAsync();
        }

        Hook.Events.Should()
            .Contain(
                nameof(ITrainLifecycleHook.OnCancelled),
                "the hook's publish took the caller's token, so the cancellation cancelled the "
                    + "very notification it was reporting, and the runner swallowed the error"
            );
    }

    [Test]
    public async Task A_run_that_completes_despite_cancellation_still_delivers_its_completed_event()
    {
        var train = Scope.ServiceProvider.GetRequiredService<IUninterruptibleHookTrain>();
        using var cts = new CancellationTokenSource();

        var runTask = train.Run(Unit.Default, cts.Token);
        await Task.WhenAny(Probe.Started.Task, runTask).WaitAsync(TimeSpan.FromSeconds(15));

        // Cancel while the body is mid-flight, then let it finish anyway, as a downstream call with
        // no cancellation support would.
        await cts.CancelAsync();
        Probe.Downstream.TrySetResult();
        await runTask;

        Hook.Events.Should()
            .Contain(
                nameof(ITrainLifecycleHook.OnCompleted),
                "work that finished is reported as finished whatever the caller's token says, and "
                    + "the report is not part of the work the caller could cancel"
            );
    }

    /// <summary>
    /// Records terminal events, and fails the way a real publish does when handed a cancelled
    /// token: <c>BasicPublishAsync(cancellationToken: ct)</c> throws rather than sending.
    /// </summary>
    private sealed class RecordingHook : ITrainLifecycleHook
    {
        public List<string> Events { get; } = [];

        public void Reset() => Events.Clear();

        public Task OnCompleted(Metadata metadata, CancellationToken ct) =>
            Record(nameof(OnCompleted), ct);

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            Record(nameof(OnFailed), ct);

        public Task OnCancelled(Metadata metadata, CancellationToken ct) =>
            Record(nameof(OnCancelled), ct);

        private Task Record(string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Events.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHookFactory(RecordingHook hook) : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => hook;
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

    private class ParkedHookTrain : ServiceTrain<Unit, Unit>, IParkedHookTrain
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

    private class UninterruptibleHookTrain : ServiceTrain<Unit, Unit>, IUninterruptibleHookTrain
    {
        protected override async Task<Either<Exception, Unit>> Junctions()
        {
            Probe.Started.TrySetResult();
            await Probe.Downstream.Task;

            return Resolve();
        }
    }

    private interface IParkedHookTrain : IServiceTrain<Unit, Unit> { }

    private interface IUninterruptibleHookTrain : IServiceTrain<Unit, Unit> { }
}
