using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// What the caller's cancellation token reaches on the synchronous execution path — the token
/// a GraphQL resolver supplies as <c>ctx.RequestAborted</c>, so these are the semantics a
/// disconnecting or timing-out HTTP client gets.
///
/// <para>Cancellation here is cooperative: it reaches the train as
/// <see cref="Train{TIn,TOut}.CancellationToken"/> and stops only the work that observes it.
/// What the execution record then says is Trax.Effect's contract, pinned by
/// <c>CancelledOutcomePersistenceTests</c> there rather than duplicated here.</para>
/// </summary>
public class CancellationContractTests : TestSetup
{
    public override async Task TestSetUp()
    {
        await base.TestSetUp().ConfigureAwait(false);
        CancelProbe.Reset();
    }

    [Test]
    public async Task Run_WithAnAlreadyCancelledToken_NeverStartsTheTrainAndRecordsNothing()
    {
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var run = async () =>
            await execution
                .RunAsync(typeof(ICooperativeTrain).FullName.NotNull(), "{}", cts.Token)
                .ConfigureAwait(false);

        await run.Should().ThrowAsync<OperationCanceledException>();

        CancelProbe.Started.Task.IsCompleted.Should().BeFalse();

        var context = NewContext();
        await using var contextDisposal = context.ConfigureAwait(false);
        var rows = await context.Metadatas.AsNoTracking().ToListAsync(CancellationToken.None);
        rows.Should().BeEmpty("nothing ran, so there is no execution to account for");
    }

    [Test]
    public async Task Run_WhenTheCallerCancels_StopsATrainThatObservesTheToken()
    {
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        using var cts = new CancellationTokenSource();

        var runTask = execution.RunAsync(
            typeof(ICooperativeTrain).FullName.NotNull(),
            "{}",
            cts.Token
        );
        try
        {
            await AwaitSignalAsync(CancelProbe.Started.Task, runTask, "the train started");

            await cts.CancelAsync();

            var awaiting = async () =>
                await runTask
                    .WaitAsync(SignalTimeout, TimeProvider.System, CancellationToken.None)
                    .ConfigureAwait(false);
            await awaiting.Should().ThrowAsync<OperationCanceledException>();

            CancelProbe
                .RanToCompletion.Should()
                .BeFalse(
                    "a train that passes the token to its downstream call is genuinely stopped"
                );
        }
        finally
        {
            // The train parks until the token fires, so a failure above must still cancel it.
            await cts.CancelAsync();
            await DrainAsync(runTask);
        }
    }

    [Test]
    public async Task Run_WhenTheTrainIgnoresTheToken_TheDownstreamWorkCompletesAnyway()
    {
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        using var cts = new CancellationTokenSource();

        var runTask = execution.RunAsync(
            typeof(IIndifferentTrain).FullName.NotNull(),
            "{}",
            cts.Token
        );
        try
        {
            await AwaitSignalAsync(CancelProbe.Started.Task, runTask, "the train started");

            await cts.CancelAsync();

            runTask
                .IsCompleted.Should()
                .BeFalse("cancelling the caller's token does not interrupt work that ignores it");

            // The downstream system answers after the caller has already given up.
            CancelProbe.Downstream.SetResult();

            // Whether the call then throws or returns the result is Trax.Effect's to decide, and
            // it changed when the terminal write stopped running on the caller's token — see
            // CancelledOutcomePersistenceTests there. What this repo owns is the fact above it:
            // the work was not interruptible, so it ran to completion after the caller had given
            // up.
            try
            {
                await runTask.WaitAsync(SignalTimeout, TimeProvider.System, CancellationToken.None);
            }
            catch (OperationCanceledException) { }

            CancelProbe
                .RanToCompletion.Should()
                .BeTrue("the write landed downstream after the caller stopped waiting for it");
        }
        finally
        {
            // The train ignores the token, so only the downstream gate can let it finish.
            CancelProbe.Downstream.TrySetResult();
            await DrainAsync(runTask);
        }
    }

    private IDataContext NewContext() =>
        (IDataContext)
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>().Create();

    internal static class CancelProbe
    {
        public static TaskCompletionSource Started { get; private set; } = Fresh();

        public static TaskCompletionSource Downstream { get; private set; } = Fresh();

        public static bool RanToCompletion { get; set; }

        public static void Reset()
        {
            Started = Fresh();
            Downstream = Fresh();
            RanToCompletion = false;
        }

        private static TaskCompletionSource Fresh() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A train that hands the caller's token to its downstream call.</summary>
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
    )]
    internal sealed class CooperativeTrain : ServiceTrain<CooperativeInput, Unit>, ICooperativeTrain
    {
        protected override async Task<Either<Exception, Unit>> Junctions()
        {
            var parked = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var registration = CancellationToken.Register(() =>
                parked.TrySetCanceled(CancellationToken.None)
            );
            await using (registration.ConfigureAwait(false))
            {
                CancelProbe.Started.TrySetResult();
                await parked.Task.ConfigureAwait(false);

                CancelProbe.RanToCompletion = true;
                return Resolve();
            }
        }
    }

    /// <summary>A train whose downstream call takes no token, as an un-cancellable SDK would.</summary>
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
    )]
    internal sealed class IndifferentTrain : ServiceTrain<IndifferentInput, Unit>, IIndifferentTrain
    {
        protected override async Task<Either<Exception, Unit>> Junctions()
        {
            CancelProbe.Started.TrySetResult();
            await CancelProbe.Downstream.Task.ConfigureAwait(false);

            CancelProbe.RanToCompletion = true;
            return Resolve();
        }
    }

    internal sealed record CooperativeInput;

    internal sealed record IndifferentInput;

    internal interface ICooperativeTrain : IServiceTrain<CooperativeInput, Unit>;

    internal interface IIndifferentTrain : IServiceTrain<IndifferentInput, Unit>;
}
