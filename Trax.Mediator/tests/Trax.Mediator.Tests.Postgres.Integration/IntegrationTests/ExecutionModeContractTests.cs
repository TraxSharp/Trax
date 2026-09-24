using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// Pins the observable contract of the two execution modes behind the GraphQL
/// <c>ExecutionMode</c> enum, so callers can tell what a returned id means.
///
/// <para>RUN awaits the train's downstream work; the caller holds the connection for its whole
/// duration and the <c>metadataId</c> it receives names a settled execution. QUEUE returns a
/// receipt before the train has run at all, and its <c>externalId</c> is the correlation key
/// the eventual execution is recorded under.</para>
/// </summary>
public class ExecutionModeContractTests : TestSetup
{
    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        DownstreamProbe.Reset();
    }

    [Test]
    public async Task Run_DoesNotReturn_UntilTheTrainsDownstreamWorkHasCompleted()
    {
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var runTask = execution.RunAsync(
            typeof(IGatedDownstreamTrain).FullName!,
            "{}",
            CancellationToken.None
        );

        try
        {
            // Synchronise on the train reaching its downstream call rather than on a duration:
            // at this instant the work is provably outstanding.
            await AwaitSignalAsync(
                DownstreamProbe.ReachedDownstream.Task,
                runTask,
                "the train reached its downstream call"
            );

            runTask
                .IsCompleted.Should()
                .BeFalse(
                    "RUN awaits downstream work — a caller that timed out here would abandon a live execution"
                );
            DownstreamProbe.CompletedAt.Should().BeNull();

            DownstreamProbe.Downstream.SetResult();
            await runTask.WaitAsync(SignalTimeout);

            DownstreamProbe
                .CompletedAt.Should()
                .NotBeNull("the downstream work completed before RUN returned");
        }
        finally
        {
            DownstreamProbe.Downstream.TrySetResult();
            await DrainAsync(runTask);
        }
    }

    [Test]
    public async Task Run_WritesTheMetadataRow_BeforeTheWorkItRecordsHasHappened()
    {
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var runTask = execution.RunAsync(
            typeof(IGatedDownstreamTrain).FullName!,
            "{}",
            CancellationToken.None
        );
        try
        {
            await AwaitSignalAsync(
                DownstreamProbe.ReachedDownstream.Task,
                runTask,
                "the train reached its downstream call"
            );

            var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
            using (var context = (IDataContext)factory.Create())
            {
                var inFlight = await context
                    .Metadatas.AsNoTracking()
                    .SingleAsync(m => m.Name == typeof(IGatedDownstreamTrain).FullName);

                inFlight
                    .TrainState.Should()
                    .NotBe(
                        TrainState.Completed,
                        "the row is opened before the train runs, so a concurrent reader sees an "
                            + "unfinished execution — which is not the same as RUN having returned"
                    );
                inFlight.EndTime.Should().BeNull();
            }

            DownstreamProbe.Downstream.SetResult();
            await runTask.WaitAsync(SignalTimeout);
        }
        finally
        {
            DownstreamProbe.Downstream.TrySetResult();
            await DrainAsync(runTask);
        }
    }

    [Test]
    public async Task Run_WhenItReturns_TheMetadataRowIsAlreadySettled()
    {
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        DownstreamProbe.Downstream.SetResult();

        var result = await execution.RunAsync(
            typeof(IGatedDownstreamTrain).FullName!,
            "{}",
            CancellationToken.None
        );

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        var metadata = await context
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == result.MetadataId);

        metadata
            .TrainState.Should()
            .Be(
                TrainState.Completed,
                "a returned metadataId names an outcome, not a receipt — nothing settles after the response"
            );
        metadata.EndTime.Should().NotBeNull();
        metadata.ExternalId.Should().Be(result.ExternalId);
    }

    [Test]
    public async Task Queue_Returns_BeforeTheTrainHasExecuted()
    {
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await execution.QueueAsync(
            typeof(IGatedDownstreamTrain).FullName!,
            "{}",
            ct: CancellationToken.None
        );

        // The downstream gate was never released, and QUEUE returned anyway: nothing ran.
        DownstreamProbe.ReachedDownstream.Task.IsCompleted.Should().BeFalse();
        DownstreamProbe.CompletedAt.Should().BeNull();

        result.WorkQueueId.Should().BeGreaterThan(0);
        result.ExternalId.Should().NotBeNullOrEmpty();

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)factory.Create();

        var entry = await context
            .WorkQueues.AsNoTracking()
            .FirstAsync(w => w.Id == result.WorkQueueId);
        entry.Status.Should().Be(WorkQueueStatus.Queued);
        entry.ExternalId.Should().Be(result.ExternalId);
        entry
            .MetadataId.Should()
            .BeNull("no execution exists yet — the scheduler links one when it dispatches");

        var executions = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.ExternalId == result.ExternalId)
            .ToListAsync();
        executions.Should().BeEmpty();
    }

    /// <summary>
    /// Stands in for a slow external system of record. The train parks on
    /// <see cref="Downstream"/> until the test releases it, so "is the downstream work
    /// finished?" is a question the test answers rather than races.
    /// </summary>
    internal static class DownstreamProbe
    {
        public static TaskCompletionSource ReachedDownstream { get; private set; } = Fresh();

        public static TaskCompletionSource Downstream { get; private set; } = Fresh();

        public static DateTime? CompletedAt { get; set; }

        public static void Reset()
        {
            ReachedDownstream = Fresh();
            Downstream = Fresh();
            CompletedAt = null;
        }

        private static TaskCompletionSource Fresh() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal class GatedDownstreamTrain
        : ServiceTrain<GatedDownstreamInput, Unit>,
            IGatedDownstreamTrain
    {
        protected override async Task<Either<Exception, Unit>> Junctions()
        {
            DownstreamProbe.ReachedDownstream.TrySetResult();
            await DownstreamProbe.Downstream.Task;
            DownstreamProbe.CompletedAt = DateTime.UtcNow;

            return Resolve();
        }
    }

    internal record GatedDownstreamInput;

    internal interface IGatedDownstreamTrain : IServiceTrain<GatedDownstreamInput, Unit> { }
}
