using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.LifecycleHookRunner;

namespace Trax.Scheduler.Services.RunOutcomes;

/// <summary>
/// Publishes the terminal lifecycle event of a run that the scheduler recorded <c>Failed</c> or
/// <c>Cancelled</c> itself, with no train running to publish it: a run reaped as stale, failed at
/// dispatch or at submit, failed on startup recovery, or cancelled before it started. The event
/// goes through the same lifecycle hooks a train's own outcome goes through (<c>OnFailed</c> or
/// <c>OnCancelled</c>, then <c>OnStateChanged</c>), so a subscriber following the run is told it
/// ended rather than waiting forever.
/// </summary>
/// <remarks>
/// <para>
/// Every caller publishes only after its write has committed, and only for the runs its own
/// conditional write moved into the terminal state, so a run is published once however often a
/// reaper passes over it. Each run is read back from the store and given to hooks built in a scope
/// of its own (the run's scope, as a train's hooks are).
/// </para>
/// <para>
/// The hooks swallow their own exceptions. A hook that hangs, whether or not it honours its token,
/// is abandoned after <see cref="PublishTimeout"/>, and any other failure to publish is logged:
/// the outcome is already recorded on the run's row either way, and the caller (a reaper, the
/// dispatcher, a runner) must carry on.
/// </para>
/// </remarks>
internal static class OutOfTrainOutcomes
{
    /// <summary>
    /// How long the lifecycle hooks may take to receive one run before the publisher stops waiting
    /// for them. Settable so a test can shorten it.
    /// </summary>
    internal static TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Publishes <c>Failed</c> for each of <paramref name="runIds"/> whose row is <c>Failed</c>.
    /// </summary>
    /// <param name="services">A provider to build each run's scope from.</param>
    /// <param name="runIds">The runs the caller's committed write failed.</param>
    /// <param name="logger">Where a failure to publish is logged.</param>
    /// <param name="describe">
    /// The failure the hooks are given for a run, which may also adjust what the run's row shows
    /// them. Defaults to <see cref="RecordedFailure"/>, the failure the row records.
    /// </param>
    /// <param name="timeout">Overrides <see cref="PublishTimeout"/>.</param>
    /// <param name="parallelism">
    /// How many runs are published at once. One, the default, publishes them in order; each run's
    /// read and hooks are bounded by the timeout either way.
    /// </param>
    internal static Task PublishFailedAsync(
        IServiceProvider services,
        IEnumerable<long> runIds,
        ILogger logger,
        Func<Metadata, Exception>? describe = null,
        TimeSpan? timeout = null,
        int parallelism = 1
    ) =>
        PublishAsync(
            services,
            runIds,
            TrainState.Failed,
            logger,
            timeout,
            (hooks, run, ct) => hooks.OnFailed(run, (describe ?? RecordedFailure)(run), ct),
            parallelism
        );

    /// <summary>
    /// Publishes <c>Cancelled</c> for each of <paramref name="runIds"/> whose row is
    /// <c>Cancelled</c>.
    /// </summary>
    /// <param name="services">A provider to build each run's scope from.</param>
    /// <param name="runIds">The runs the caller's committed write cancelled.</param>
    /// <param name="logger">Where a failure to publish is logged.</param>
    internal static Task PublishCancelledAsync(
        IServiceProvider services,
        IEnumerable<long> runIds,
        ILogger logger
    ) =>
        PublishAsync(
            services,
            runIds,
            TrainState.Cancelled,
            logger,
            timeout: null,
            (hooks, run, ct) => hooks.OnCancelled(run, ct)
        );

    /// <summary>
    /// The failure a lifecycle event carries for a run whose <c>Failed</c> outcome the scheduler
    /// wrote: a <see cref="TrainException"/> carrying the row's own failure type, junction, reason
    /// and class as <see cref="TrainExceptionData"/>, the shape a train's own failure carries.
    /// </summary>
    internal static Exception RecordedFailure(Metadata run)
    {
        var message = run.FailureReason ?? "The run failed outside a train.";
        var exception = new TrainException(message);
        exception.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = run.Name,
            TrainExternalId = run.ExternalId,
            Type = run.FailureException ?? nameof(TrainException),
            Junction = run.FailureJunction ?? nameof(OutOfTrainOutcomes),
            Message = message,
            FailureClass = run.FailureClass,
        };
        return exception;
    }

    private static async Task PublishAsync(
        IServiceProvider services,
        IEnumerable<long> runIds,
        TrainState state,
        ILogger logger,
        TimeSpan? timeout,
        Func<ILifecycleHookRunner, Metadata, CancellationToken, Task> publish,
        int parallelism = 1
    )
    {
        var bound = timeout ?? PublishTimeout;

        async Task PublishBoundedAsync(long runId)
        {
            try
            {
                // The read and the hooks share one bound, so neither an unreachable store nor a
                // hook that ignores its token holds the caller up for longer.
                using var bounded = new CancellationTokenSource(bound);
                await PublishOneAsync(services, runId, state, publish, bounded.Token)
                    .WaitAsync(bound, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Could not publish the {TrainState} outcome of Metadata {MetadataId}; the "
                        + "outcome is recorded on its row",
                    state,
                    runId
                );
            }
        }

        if (parallelism <= 1)
        {
            foreach (var runId in runIds)
                await PublishBoundedAsync(runId);
            return;
        }

        // Each run in a scope of its own, as in order; a slow hook holds up only its own run.
        await Parallel.ForEachAsync(
            runIds,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            async (runId, _) => await PublishBoundedAsync(runId)
        );
    }

    private static async Task PublishOneAsync(
        IServiceProvider services,
        long runId,
        TrainState state,
        Func<ILifecycleHookRunner, Metadata, CancellationToken, Task> publish,
        CancellationToken ct
    )
    {
        using var scope = services.CreateScope();

        // Transient, and disposed with the scope. No hooks or no store: nothing to publish.
        var hooks = scope.ServiceProvider.GetService<ILifecycleHookRunner>();
        var factory = scope.ServiceProvider.GetService<IDataContextProviderFactory>();
        if (hooks is null || factory is null)
            return;

        Metadata? run;
        using (var context = await factory.CreateDbContextAsync(ct))
            run = await context
                .Metadatas.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == runId, ct);

        if (run is null || run.TrainState != state)
            return;

        await publish(hooks, run, ct);
    }
}
