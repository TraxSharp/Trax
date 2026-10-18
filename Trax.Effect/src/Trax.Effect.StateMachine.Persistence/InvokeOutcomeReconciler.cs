using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.InvokedRunListener;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The guarantee that every invoked run's outcome reaches its machine: a hosted service, started on every host that
/// registers machines, that sweeps for the rows whose invoked run has ended and delivers each through
/// <see cref="InvokeOutcomeDelivery"/>. The lifecycle hook is the fast path, on the host that ran the train; a crash
/// between the run's terminal write and its hook, a run the reaper failed, a cancel before dispatch, or a run on a
/// host that registers no machines, all reach the machine through this sweep instead.
/// </summary>
/// <remarks>
/// <para>It sweeps every <see cref="StateMachineOptions.InvokeOutcomeSweepInterval"/>. On Postgres a trigger
/// notifies when an invoked run ends: the notices are drained, deduplicated and checked in one query for the runs
/// that ended on machines this host handles, and those are delivered at once. While it hears the notices it sweeps
/// only every <see cref="StateMachineOptions.InvokeOutcomeSweepIntervalWhileListening"/>, and it sweeps in full
/// whenever it subscribes again after losing them, which covers what ended while it was not listening. Only
/// machines this host registers, and only those that invoke trains, are swept; a host with none does nothing.</para>
/// <para>Any number of hosts may sweep at once. Each delivery is one conditional update on the token, so the
/// outcome is applied by whichever matches first and is a no-transition for every other.</para>
/// <para>It also fires due timers (<see cref="FireDueTimersOnce"/>), on every host that registers machines, those with
/// timers and no invoked trains included: each sweep fires a batch, and between sweeps it wakes for the next due
/// timer, read on the database's clock, rather than waiting out the interval.</para>
/// <para>A row this host cannot apply an outcome to (<see cref="InvokeDelivery.NotHere"/>: a snapshot it cannot
/// read) is tried again only after a back-off that doubles from <see cref="NotHereBackoff"/> up to
/// <see cref="NotHereBackoffCap"/>, so a row no host can read is logged rarely rather than at every sweep.</para>
/// </remarks>
internal sealed class InvokeOutcomeReconciler(
    IEnumerable<IMachine> machines,
    IServiceScopeFactory scopes,
    StateMachineOptions options,
    ILogger<InvokeOutcomeReconciler> logger,
    IInvokedRunListener? listener = null
) : BackgroundService
{
    /// <summary>How many tokens one page of the sweep, or one batch of notices, reads. Settable for tests.</summary>
    internal int PageSize { get; set; } = 200;

    /// <summary>
    /// How many notices wait to be read at most. A notice that finds the buffer full is dropped, and a full sweep
    /// is asked for instead, which finds its run.
    /// </summary>
    internal const int NoticeCapacity = 1000;

    /// <summary>The first wait before a row this host could not apply an outcome to is tried again.</summary>
    internal static readonly TimeSpan NotHereBackoff = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait before a row this host could not apply an outcome to is tried again.</summary>
    internal static readonly TimeSpan NotHereBackoffCap = TimeSpan.FromHours(1);

    /// <summary>The first wait before subscribing again after the notices were lost; it doubles up to the cap.</summary>
    internal static TimeSpan ResubscribeDelay { get; set; } = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ResubscribeDelayCap = TimeSpan.FromSeconds(30);

    // The soonest the sweep wakes again for a timer, so a timer that is due but waits (on an effect's claim) does not
    // keep it spinning.
    private static readonly TimeSpan MinTimerWake = TimeSpan.FromSeconds(1);

    private readonly Lazy<HashSet<string>> _invoking = new(() =>
        machines
            .OfType<IMachineInternals>()
            .Where(m => m.WaitsOnWork)
            .Select(m => ((IMachine)m).Name)
            .ToHashSet(StringComparer.Ordinal)
    );

    // The machines another machine's state may spawn, with the states each ends in: the children whose end the sweep
    // delivers to a parent still waiting on it.
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> _ending = new(
        () =>
            machines
                .OfType<IMachineInternals>()
                .Where(m => m.Ends.Count > 0)
                .GroupBy(m => ((IMachine)m).Name, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyCollection<string>)g.First().Ends.Keys.ToList(),
                    StringComparer.Ordinal
                )
    );

    // Every machine this host registers, by name: the ones whose due timers it fires, and whose stale due times (of a
    // timer a deploy removed) it clears.
    private readonly Lazy<IReadOnlyDictionary<string, IMachineInternals>> _byName = new(() =>
        machines
            .OfType<IMachineInternals>()
            .GroupBy(m => ((IMachine)m).Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
    );

    // The rows this host could not read when their timer was due, by row id, and when each may be tried again.
    private readonly ConcurrentDictionary<long, Backoff> _timerNotHere = new();

    // The rows whose timer waited on a live effect claim, by row id, and when each is read again.
    private readonly ConcurrentDictionary<long, Backoff> _timerWaiting = new();

    // The rows whose fire threw, by row id: the revision it threw at, how many times, and when it may be tried again.
    private readonly ConcurrentDictionary<long, TimerError> _timerErrors = new();

    // DatabaseFailures counts the throws that were the database's, which back the row off but are not attempts.
    private readonly record struct TimerError(
        long Revision,
        int Attempts,
        DateTimeOffset Until,
        int DatabaseFailures = 0
    );

    /// <summary>
    /// How many times a fire that throws is tried before its due time is cleared. Settable for tests. The count is kept
    /// by each host, in memory: every host counts its own attempts, so with several hosts a row is tried up to this
    /// many times on each before one clears it, and a host that restarts starts its count over. A failure of the
    /// database rather than of the row (a <see cref="System.Data.Common.DbException"/> or a <see cref="TimeoutException"/>
    /// anywhere in the exception's chain) is backed off and metered but is not an attempt, so a database that keeps
    /// failing never clears a timer; such a row is tried again after a back-off that doubles from
    /// <see cref="TimerErrorBackoff"/> up to <see cref="NotHereBackoffCap"/>, until it fires.
    /// </summary>
    internal int TimerFireAttempts { get; set; } = 5;

    /// <summary>The first wait before a fire that threw is tried again; it doubles up to <see cref="NotHereBackoffCap"/>.</summary>
    internal static readonly TimeSpan TimerErrorBackoff = TimeSpan.FromSeconds(30);

    /// <summary>The longest a row waiting on a live effect claim goes before it is read again.</summary>
    internal static readonly TimeSpan WaitingRecheck = TimeSpan.FromSeconds(30);

    /// <summary>Raised after each timer this reconciler handles, with what it did. For tests.</summary>
    internal event Action<TimerFireResult>? TimerFired;

    /// <summary>Awaited after a due row is read and before its transaction begins. For tests.</summary>
    internal Func<DueRow, Task>? AfterRead { get; set; }

    /// <summary>
    /// Awaited inside a due row's transaction, after the row is locked and found held by no effect claim, before the
    /// fire is written. For tests.
    /// </summary>
    internal Func<DueRow, Task>? AfterClaimCheck { get; set; }

    private readonly TaskCompletionSource _listening = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private readonly TaskCompletionSource _firstSweep = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    // The rows this host could not apply an outcome to, by token, and when each may be tried again.
    private readonly ConcurrentDictionary<string, Backoff> _notHere = new(StringComparer.Ordinal);

    // Set when a full sweep is asked for: a resubscription, or a notice dropped from a full buffer.
    private int _sweepAsked;

    // Whether the subscription to the notices is live.
    private volatile bool _hearing;

    /// <summary>Raised after each delivery this reconciler makes, with what it did. For tests.</summary>
    internal event Action<string, InvokeDelivery>? Delivered;

    /// <summary>Raised as the hosted service starts each full sweep. For tests.</summary>
    internal event Action? Sweeping;

    /// <summary>Raised each time the hosted service subscribes to the provider's notices. For tests.</summary>
    internal event Action? Subscribed;

    /// <summary>The clock the back-off of an unreadable row is read on. Settable for tests.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Completes once the reconciler first hears the provider's notices. For tests.</summary>
    internal Task Listening => _listening.Task;

    /// <summary>Completes once the hosted service's first sweep has ended. For tests.</summary>
    internal Task FirstSweep => _firstSweep.Task;

    /// <summary>Whether <paramref name="machine"/> is registered here and invokes trains.</summary>
    internal bool Handles(string machine) => _invoking.Value.Contains(machine);

    /// <summary>
    /// One full sweep: the rows of the machines this host handles whose invoked run has ended, page by page in
    /// token order, each delivered. Rows whose run is still going are never read past the query that finds the
    /// ended ones. A failure delivering one is logged and the sweep carries on.
    /// </summary>
    internal async Task<IReadOnlyList<InvokeDelivery>> SweepOnce(
        CancellationToken cancellationToken
    )
    {
        var results = new List<InvokeDelivery>();
        if (_invoking.Value.Count == 0)
            return results;

        ForgetExpiredBackoffs();

        string? after = null;
        do
        {
            var page = await EndedPage(after, among: null, cancellationToken);
            await DeliverAll(page.Tokens, results, cancellationToken);
            after = page.Next;
        } while (after is not null);

        // The children that ended while nothing delivered it: their write's own delivery failed, or was never made.
        if (_ending.Value.Count > 0)
            do
            {
                EndedPage page;
                await using (var scope = scopes.CreateAsyncScope())
                    page = await scope
                        .ServiceProvider.GetRequiredService<InvokeOutcomeDelivery>()
                        .ChildrenEnded(_ending.Value, PageSize, after, cancellationToken);
                await DeliverAll(page.Tokens, results, cancellationToken);
                after = page.Next;
            } while (after is not null);

        return results;
    }

    /// <summary>
    /// One sweep of due timers: the rows of the machines this host registers whose timer is due on the database's
    /// clock, soonest first, read in pages of <see cref="PageSize"/> until that many have moved or none is left. Each
    /// row is fired in a transaction of its own, which first locks it (on Postgres <c>FOR UPDATE SKIP LOCKED</c>, so a
    /// row another host's sweep holds is passed over) and checks it is still the revision that was read; a fire is a
    /// conditional update on the row's state and revision (<see cref="TimerFireResult.Fired"/>), and a row written
    /// since it was read loses (<see cref="TimerFireResult.Lost"/>). A row this host cannot read, written by a newer
    /// definition, is left to a host that can and tried here again only after a back-off
    /// (<see cref="TimerFireResult.NotHere"/>). A row whose state no longer has a timer is cleared and never fired
    /// (<see cref="TimerFireResult.Cleared"/>); one held by an effect that runs from its state waits for it, and is not
    /// read again until a short back-off has passed (<see cref="TimerFireResult.Waiting"/>): the effect's claim holds
    /// the timer while it is in flight, however long it outlives its lease, and once it has a receipt for the
    /// instance's current content that no advance has applied yet; one whose timer cannot be applied
    /// is cleared and counted (<see cref="TimerFireResult.Failed"/>). A fire that throws rolls back that row alone,
    /// is counted, and is tried again after a back-off (<see cref="TimerFireResult.Retrying"/>), until after
    /// <see cref="TimerFireAttempts"/> attempts its due time is cleared and it is counted as failed. A timer leaving
    /// an invoking state clears the token and cancels the run in the same transaction.
    /// </summary>
    internal async Task<IReadOnlyList<TimerFireResult>> FireDueTimersOnce(
        CancellationToken cancellationToken
    )
    {
        var results = new List<TimerFireResult>();
        if (_byName.Value.Count == 0)
            return results;

        ForgetExpiredTimerBackoffs();
        await FinishAbandonedEffectsOnce(cancellationToken);
        // The rows read in this sweep, and those backing off: none is read twice, so a page of rows that cannot move
        // now (waiting on a claim, or lost to another host) never keeps the sweep from the rows behind it.
        var seen = new HashSet<long>(TimerBackingOff());
        var moved = 0;
        while (moved < PageSize)
        {
            var page = await DuePage(seen, cancellationToken);
            if (page.Count == 0)
                break;
            foreach (var row in page)
            {
                seen.Add(row.RowId);
                var result = await FireIsolated(row, cancellationToken);
                results.Add(result);
                TimerFired?.Invoke(result);
                if (result.Moved && ++moved >= PageSize)
                    break;
            }
        }
        return results;
    }

    // The rows whose abandoned effect this host could not finish (its effect is not registered here, or finishing it
    // threw), by row id, and when each is read again.
    private readonly ConcurrentDictionary<long, DateTimeOffset> _effectNotHere = new();

    /// <summary>How many abandoned effects one sweep reads at most.</summary>
    internal const int AbandonedEffectBatch = 50;

    // Finishes the effects pending on users' rows of the machines this host registers whose claimant's lease has passed
    // on the database's clock: each is taken over, run again under the same key and recorded as the answer to the
    // request that sent it, through the machine's effect runner. A host without the effect leaves the row for one that
    // has it. Each one found is counted on EffectsAbandoned.
    private async Task FinishAbandonedEffectsOnce(CancellationToken cancellationToken)
    {
        var withEffects = _byName
            .Value.Where(m => ((IMachine)m.Value).HasEffect)
            .Select(m => m.Key)
            .ToList();
        if (withEffects.Count == 0)
            return;
        var now = Time.GetUtcNow();
        foreach (var (rowId, until) in _effectNotHere)
            if (until <= now)
                _effectNotHere.TryRemove(rowId, out _);

        IReadOnlyList<AbandonedEffect> abandoned;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            abandoned = await new EfSnapshotStore(
                services.GetRequiredService<IDataContext>(),
                services.GetService<ISqlDialect>()
            ).ReadAbandonedEffects(withEffects, AbandonedEffectBatch, cancellationToken);
        }

        foreach (var effect in abandoned)
        {
            StateMachineMeter.EffectsAbandoned.Add(
                1,
                new KeyValuePair<string, object?>("machine", effect.Machine)
            );
            if (_effectNotHere.ContainsKey(effect.RowId))
                continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var runner =
                    scope
                        .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                        .EffectRunner(effect.Machine) as IActorEffectRunner;
                if (runner is null)
                {
                    _effectNotHere[effect.RowId] = now + WaitingRecheck;
                    continue;
                }
                await runner.FinishAbandoned(effect.UserKey, effect.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // An effect this host cannot resolve (not registered here) or that failed again is left pending for
                // a later sweep, here or on another host.
                _effectNotHere[effect.RowId] = now + WaitingRecheck;
                logger.LogWarning(
                    ex,
                    "The effect pending on {Machine} {Id} past its lease could not be finished here; it is tried "
                        + "again later.",
                    effect.Machine,
                    effect.Id
                );
            }
        }
    }

    private async Task<IReadOnlyList<DueRow>> DuePage(
        IReadOnlyCollection<long> skip,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await using var context = await services
            .GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(cancellationToken);
        return await services
            .GetRequiredService<InvokeOutbox>()
            .StoreOver(context, services.GetService<ISqlDialect>())
            .ReadDue(_byName.Value.Keys.ToList(), skip, PageSize, cancellationToken);
    }

    // Fires one due row in a transaction of its own. A throw rolls back this row only: it is counted, backed off and,
    // after TimerFireAttempts throws, cleared.
    private async Task<TimerFireResult> FireIsolated(
        DueRow row,
        CancellationToken cancellationToken
    )
    {
        if (AfterRead is { } afterRead)
            await afterRead(row);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var outbox = services.GetRequiredService<InvokeOutbox>();
            await using var context = await services
                .GetRequiredService<IDataContextProviderFactory>()
                .CreateDbContextAsync(cancellationToken);
            var store = outbox.StoreOver(context, services.GetService<ISqlDialect>());

            TimerFireResult result;
            var transaction = await context.BeginTransaction(cancellationToken);
            try
            {
                // Locked before anything is decided, so neither a client's write nor an effect runner's check can
                // slip between what the fire reads and what it writes.
                result = await store.LockDue(row, cancellationToken)
                    ? await FireOne(context, store, outbox, row, cancellationToken)
                    : new TimerFireResult(row.Id, TimerFireResult.Lost);
                await transaction.Commit();
                services.GetService<SnapshotWriteSignal>()?.Written();
            }
            catch
            {
                try
                {
                    await transaction.Rollback();
                }
                catch (Exception)
                {
                    // The failure that led here is the one to report; the database rolls back a lost connection.
                }
                throw;
            }
            finally
            {
                transaction.Dispose();
            }

            _timerErrors.TryRemove(row.RowId, out _);

            // A timer may have ended a child: its parent is told now, rather than at the next sweep.
            if (result.Outcome == TimerFireResult.Fired && _ending.Value.ContainsKey(row.Machine))
                await SpawnedEnds.Notify(
                    scopes,
                    new DraftOwner(row.OwnerKind, row.UserKey),
                    row.Machine,
                    row.Id,
                    cancellationToken
                );
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await FireThrew(row, ex, cancellationToken);
        }
    }

    // A fire that threw is tried again after a back-off that doubles from TimerErrorBackoff, and after
    // TimerFireAttempts throws its due time is cleared, as a fire that cannot be applied is: the row stays where it
    // is, an event still moves it, and it no longer holds a place at the head of every sweep.
    private async Task<TimerFireResult> FireThrew(
        DueRow row,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var machineTag = new KeyValuePair<string, object?>("machine", row.Machine);
        StateMachineMeter.TimerFireErrors.Add(1, machineTag);
        if (DatabaseFailed(exception))
        {
            RecordTimerDatabaseFailure(row);
            logger.LogWarning(
                exception,
                "Firing the timer of {Machine} {Id} failed in the database; it is tried again after a back-off, "
                    + "and the failure does not count toward clearing it.",
                row.Machine,
                row.Id
            );
            return new TimerFireResult(row.Id, TimerFireResult.Retrying);
        }
        var attempts = RecordTimerError(row);
        if (attempts < TimerFireAttempts)
        {
            logger.LogError(
                exception,
                "Firing the timer of {Machine} {Id} threw (attempt {Attempt} of {Attempts}); it is tried again "
                    + "after a back-off.",
                row.Machine,
                row.Id,
                attempts,
                TimerFireAttempts
            );
            return new TimerFireResult(row.Id, TimerFireResult.Retrying);
        }

        bool cleared;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            await using var context = await services
                .GetRequiredService<IDataContextProviderFactory>()
                .CreateDbContextAsync(cancellationToken);
            cleared = await services
                .GetRequiredService<InvokeOutbox>()
                .StoreOver(context, services.GetService<ISqlDialect>())
                .ClearDue(row, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception clearing)
        {
            logger.LogError(
                clearing,
                "Could not clear the due time of {Machine} {Id} after its timer threw {Attempts} times; the next "
                    + "sweep tries again.",
                row.Machine,
                row.Id,
                attempts
            );
            return new TimerFireResult(row.Id, TimerFireResult.Retrying);
        }

        _timerErrors.TryRemove(row.RowId, out _);
        if (!cleared)
            return new TimerFireResult(row.Id, TimerFireResult.Lost);
        StateMachineMeter.TimerFireFailed.Add(1, machineTag);
        logger.LogError(
            exception,
            "The timer of {Machine} {Id} threw {Attempts} times and was cleared; the instance stays in {State}.",
            row.Machine,
            row.Id,
            attempts,
            row.State
        );
        return new TimerFireResult(row.Id, TimerFireResult.Failed);
    }

    private async Task<TimerFireResult> FireOne(
        IDataContext context,
        EfSnapshotStore store,
        InvokeOutbox outbox,
        DueRow row,
        CancellationToken cancellationToken
    )
    {
        var machine = _byName.Value[row.Machine];

        // A row this host cannot read (a newer definition wrote it, or it no longer reads as one of this machine's
        // snapshots) is left for a host that can, and tried here again only after a back-off.
        if (machine.Rehydrate(row.Json) is not RehydrationResult.Ok readable)
        {
            RecordTimerNotHere(row.RowId);
            return new TimerFireResult(row.Id, TimerFireResult.NotHere);
        }
        _timerNotHere.TryRemove(row.RowId, out _);
        var current = readable.Snapshot;

        // A due time the state's definition no longer gives it (a deploy removed the timer) is cleared, never fired.
        if (machine.TimerTriggerOf(current.State) is not { } trigger)
            return new TimerFireResult(
                row.Id,
                await store.ClearDue(row, cancellationToken)
                    ? TimerFireResult.Cleared
                    : TimerFireResult.Lost
            );

        // A row with an effect pending on it is never read here: the due-timer query passes it over, and the fire's
        // conditional update matches only a row with none. A claim the earlier ledger recorded holds the timer the
        // same way until the next send records or drops it.
        if (await EffectHold(store, machine, row, current, cancellationToken) is { } wait)
        {
            RecordTimerWaiting(row.RowId, wait);
            return new TimerFireResult(row.Id, TimerFireResult.Waiting);
        }
        _timerWaiting.TryRemove(row.RowId, out _);
        if (AfterClaimCheck is { } afterClaimCheck)
            await afterClaimCheck(row);

        string? failure;
        switch (machine.AdvanceTimer(current, trigger))
        {
            case AdvanceResult.Transitioned transitioned:
                var next = transitioned.Snapshot;
                failure = StorableJson.Problem(next.Context);
                if (failure is not null)
                    break;
                var write = await outbox.FireTimer(
                    context,
                    store,
                    row,
                    next,
                    leaving: machine.Entering(current.State) is not null
                        && next.State != current.State,
                    entering: machine.Entering(next.State),
                    cancellationToken
                );
                switch (write)
                {
                    case InvokeWrite.Written:
                        return new TimerFireResult(row.Id, TimerFireResult.Fired);
                    case InvokeWrite.Refused refused:
                        failure = refused.Exception?.Message ?? refused.Message;
                        break;
                    default:
                        return new TimerFireResult(row.Id, TimerFireResult.Lost);
                }
                break;
            case AdvanceResult.Rejected rejected:
                failure = $"{rejected.Reason.ToWire()}: {rejected.Detail}";
                break;
            default:
                failure = "Unknown result.";
                break;
        }

        // A timer that cannot be applied would be read and refused at every sweep, so its due time is cleared and the
        // failure counted once. The instance stays where it is, and an event still moves it.
        if (!await store.ClearDue(row, cancellationToken))
            return new TimerFireResult(row.Id, TimerFireResult.Lost);
        StateMachineMeter.TimerFireFailed.Add(
            1,
            new KeyValuePair<string, object?>("machine", row.Machine)
        );
        logger.LogWarning(
            "The timer of {Machine} {Id} in {State} could not be fired and was cleared: {Failure}",
            row.Machine,
            row.Id,
            current.State,
            failure
        );
        return new TimerFireResult(row.Id, TimerFireResult.Failed);
    }

    // How long the timer waits before the row is read again, when a claim the earlier effect-claim ledger recorded
    // before the upgrade holds it; null when none does. An effect pending on the row itself is read by the due-timer
    // query, which passes such a row over. A ledger claim from this state holds the timer:
    // - in flight while its lease is live, on the database's clock: the effect may be running; past the lease, the
    //   next send takes it over;
    // - completed, with the fingerprint of the content the instance holds now (or none): its receipt is not recorded
    //   yet, and the next send records it. A receipt for other content is never recorded here, so it does not hold.
    private static async Task<TimeSpan?> EffectHold(
        EfSnapshotStore store,
        IMachineInternals machine,
        DueRow row,
        Snapshot current,
        CancellationToken cancellationToken
    )
    {
        var keys = machine
            .EffectKeysFrom(new DraftOwner(row.OwnerKind, row.UserKey), row.Id, current.State)
            .ToList();
        if (keys.Count == 0)
            return null;
        var claims = await store.LedgerClaims(keys, cancellationToken);
        if (claims.Count == 0)
            return null;
        if (claims.Any(c => c.Receipt is null && c.LeaseLive))
            return WaitingRecheck;
        var fingerprint = SnapshotFingerprint.Of(machine.Serialize(current));
        return claims.Any(c =>
            c.Receipt is not null
            && (
                c.ContentFingerprint is null
                || string.Equals(c.ContentFingerprint, fingerprint, StringComparison.Ordinal)
            )
        )
            ? WaitingRecheck
            : null;
    }

    // A row waiting on a claim is read again after the wait it was given, at most WaitingRecheck (the effect usually
    // ends well inside its lease, and a failed one releases the claim) and at least MinTimerWake. A zero wait (a
    // receipt not yet applied) starts at MinTimerWake and doubles each time the row is found still waiting.
    private void RecordTimerWaiting(long rowId, TimeSpan wait)
    {
        var attempts = _timerWaiting.TryGetValue(rowId, out var last) ? last.Attempts + 1 : 1;
        if (wait == TimeSpan.Zero)
            wait = MinTimerWake * Math.Pow(2, Math.Min(attempts - 1, 16));
        if (wait > WaitingRecheck)
            wait = WaitingRecheck;
        if (wait < MinTimerWake)
            wait = MinTimerWake;
        _timerWaiting[rowId] = new Backoff(attempts, Time.GetUtcNow() + wait);
    }

    // Whether a fire threw because the database failed (it could not be reached, ran out of resources, was shut down,
    // timed out, or rolled the transaction back for a deadlock or a serialization failure), however it was wrapped, not
    // because of the row: such a throw says nothing about whether the timer can ever be fired. A provider error with no
    // SQLSTATE counts as the database's. One about the row's data (a constraint, a value it cannot store) is an attempt,
    // so a timer that can never be fired is cleared after its attempts rather than retried for good.
    internal static bool DatabaseFailed(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
            switch (e)
            {
                case TimeoutException:
                    return true;
                case System.Data.Common.DbException db:
                    return db.SqlState is not { Length: >= 2 } state
                        || state.StartsWith("08", StringComparison.Ordinal)
                        || state.StartsWith("40", StringComparison.Ordinal)
                        || state.StartsWith("53", StringComparison.Ordinal)
                        || state.StartsWith("57", StringComparison.Ordinal);
            }
        return false;
    }

    // Backs the row off after a database failure, doubling from TimerErrorBackoff as a throw does, without counting
    // an attempt.
    private void RecordTimerDatabaseFailure(DueRow row)
    {
        var now = Time.GetUtcNow();
        _timerErrors.AddOrUpdate(
            row.RowId,
            _ => new TimerError(row.Revision, 0, now + TimerErrorBackoff, 1),
            (_, last) =>
            {
                var failures = last.DatabaseFailures + 1;
                var wait = TimerErrorBackoff * Math.Pow(2, Math.Min(failures - 1, 16));
                return last with
                {
                    DatabaseFailures = failures,
                    Until = now + (wait > NotHereBackoffCap ? NotHereBackoffCap : wait),
                };
            }
        );
    }

    // Counts one more throw for the row and backs it off; a row written since its last throw starts over.
    private int RecordTimerError(DueRow row)
    {
        var now = Time.GetUtcNow();
        var next = _timerErrors.AddOrUpdate(
            row.RowId,
            _ => new TimerError(row.Revision, 1, now + TimerErrorBackoff),
            (_, last) =>
            {
                var attempts = last.Revision == row.Revision ? last.Attempts + 1 : 1;
                var wait = TimerErrorBackoff * Math.Pow(2, Math.Min(attempts - 1, 16));
                return new TimerError(
                    row.Revision,
                    attempts,
                    now + (wait > NotHereBackoffCap ? NotHereBackoffCap : wait)
                );
            }
        );
        return next.Attempts;
    }

    // The rows this host leaves out of its next read: unreadable here, waiting on a claim, or backing off a throw.
    private IEnumerable<long> TimerBackingOff()
    {
        var now = Time.GetUtcNow();
        return _timerNotHere
            .Where(kv => now < kv.Value.Until)
            .Select(kv => kv.Key)
            .Concat(_timerWaiting.Where(kv => now < kv.Value.Until).Select(kv => kv.Key))
            .Concat(_timerErrors.Where(kv => now < kv.Value.Until).Select(kv => kv.Key))
            .Distinct();
    }

    // The soonest a backing-off row may be read again, on this host's clock, or null when none is backing off.
    private TimeSpan? UntilBackoffEnds()
    {
        var now = Time.GetUtcNow();
        var ends = _timerNotHere
            .Values.Select(b => b.Until)
            .Concat(_timerWaiting.Values.Select(b => b.Until))
            .Concat(_timerErrors.Values.Select(e => e.Until))
            .Where(until => until > now)
            .ToList();
        return ends.Count == 0 ? null : ends.Min() - now;
    }

    private void RecordTimerNotHere(long rowId)
    {
        var now = Time.GetUtcNow();
        _timerNotHere.AddOrUpdate(
            rowId,
            _ => new Backoff(1, now + NotHereBackoff),
            (_, last) =>
            {
                var wait = NotHereBackoff * Math.Pow(2, Math.Min(last.Attempts, 16));
                return new Backoff(
                    last.Attempts + 1,
                    now + (wait > NotHereBackoffCap ? NotHereBackoffCap : wait)
                );
            }
        );
    }

    private void ForgetExpiredTimerBackoffs()
    {
        var now = Time.GetUtcNow();
        var stale = now - 2 * NotHereBackoffCap;
        foreach (var (row, backoff) in _timerNotHere)
            if (backoff.Until < stale)
                _timerNotHere.TryRemove(row, out _);
        // A waiting row is kept past its wait, so the next wait it is given grows; one that stops waiting is removed
        // when it is read.
        foreach (var (row, backoff) in _timerWaiting)
            if (backoff.Until < stale)
                _timerWaiting.TryRemove(row, out _);
        foreach (var (row, error) in _timerErrors)
            if (error.Until < stale)
                _timerErrors.TryRemove(row, out _);
    }

    /// <summary>
    /// How long until the next timer of this host's machines is due, on the database's clock, or null. A row this host
    /// is backing off (unreadable here, waiting on a claim, or after a fire that threw) counts from when its back-off
    /// ends, not from its due time, so such a row does not pace the loop at its shortest wake.
    /// </summary>
    internal async Task<TimeSpan?> UntilNextTimer(CancellationToken cancellationToken)
    {
        if (_byName.Value.Count == 0)
            return null;
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await using var context = await services
            .GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(cancellationToken);
        var due = await services
            .GetRequiredService<InvokeOutbox>()
            .StoreOver(context, services.GetService<ISqlDialect>())
            .UntilNextDue(
                _byName.Value.Keys.ToList(),
                TimerBackingOff().ToList(),
                cancellationToken
            );
        return (due, UntilBackoffEnds()) switch
        {
            ({ } a, { } b) => a < b ? a : b,
            ({ } a, null) => a,
            (null, var b) => b,
        };
    }

    /// <summary>
    /// Delivers the runs among <paramref name="tokens"/> (each once, however often it is named) that have ended on
    /// a machine this host handles, read in one query per page; the rest cost nothing more.
    /// </summary>
    internal async Task<IReadOnlyList<InvokeDelivery>> DeliverAmong(
        IReadOnlyCollection<string> tokens,
        CancellationToken cancellationToken
    )
    {
        var results = new List<InvokeDelivery>();
        if (_invoking.Value.Count == 0)
            return results;

        foreach (var chunk in tokens.Distinct(StringComparer.Ordinal).Chunk(PageSize))
        {
            var page = await EndedPage(after: null, among: chunk, cancellationToken);
            await DeliverAll(page.Tokens, results, cancellationToken);
        }

        return results;
    }

    private async Task<EndedPage> EndedPage(
        string? after,
        IReadOnlyCollection<string>? among,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<InvokeOutcomeDelivery>()
            .Ended(_invoking.Value, PageSize, after, among, cancellationToken);
    }

    private async Task DeliverAll(
        IReadOnlyList<string> tokens,
        List<InvokeDelivery> results,
        CancellationToken cancellationToken
    )
    {
        foreach (var token in tokens)
            if (!BackingOff(token) && await TryDeliver(token, cancellationToken) is { } delivered)
                results.Add(delivered);
    }

    /// <summary>Delivers the outcome of one run, in a scope of its own. Null when the delivery threw (logged).</summary>
    internal async Task<InvokeDelivery?> TryDeliver(
        string invokeToken,
        CancellationToken cancellationToken
    )
    {
        try
        {
            InvokeDelivery result;
            await using (var scope = scopes.CreateAsyncScope())
                result = await scope
                    .ServiceProvider.GetRequiredService<InvokeOutcomeDelivery>()
                    .Deliver(invokeToken, cancellationToken);
            Record(invokeToken, result);
            Delivered?.Invoke(invokeToken, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Could not deliver the outcome of invoked run {RunId}; the next sweep tries again.",
                invokeToken
            );
            return null;
        }
    }

    // A row this host cannot apply the outcome to waits longer each time before it is tried again; any other
    // result ends its wait.
    private void Record(string invokeToken, InvokeDelivery result)
    {
        if (result is not InvokeDelivery.NotHere)
        {
            _notHere.TryRemove(invokeToken, out _);
            return;
        }

        var now = Time.GetUtcNow();
        _notHere.AddOrUpdate(
            invokeToken,
            _ => new Backoff(1, now + NotHereBackoff),
            (_, last) =>
            {
                var wait = NotHereBackoff * Math.Pow(2, Math.Min(last.Attempts, 16));
                return new Backoff(
                    last.Attempts + 1,
                    now + (wait > NotHereBackoffCap ? NotHereBackoffCap : wait)
                );
            }
        );
    }

    private bool BackingOff(string invokeToken) =>
        _notHere.TryGetValue(invokeToken, out var backoff) && Time.GetUtcNow() < backoff.Until;

    // A token that is no longer live never comes back, so a wait long past its end is forgotten.
    private void ForgetExpiredBackoffs()
    {
        var stale = Time.GetUtcNow() - 2 * NotHereBackoffCap;
        foreach (var (token, backoff) in _notHere)
            if (backoff.Until < stale)
                _notHere.TryRemove(token, out _);
    }

    private readonly record struct Backoff(int Attempts, DateTimeOffset Until);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_byName.Value.Count == 0)
            return;

        // A token asks for that one run; a null asks for a full sweep. Bounded: a notice that finds it full is
        // dropped and a full sweep asked for instead (see Notice).
        var wake = Channel.CreateBounded<string?>(
            new BoundedChannelOptions(NoticeCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            }
        );
        var listening = listener is null
            ? Task.CompletedTask
            : Listen(listener, wake.Writer, stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _sweepAsked, 0);
                Sweeping?.Invoke();
                try
                {
                    await SweepOnce(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "The sweep for invoked runs' outcomes failed; the next one tries again."
                    );
                }

                TimeSpan? nextTimer = null;
                try
                {
                    // A full batch may leave more due behind it, so the next one is fired at once.
                    while ((await FireDueTimersOnce(stoppingToken)).Count(r => r.Moved) >= PageSize)
                        stoppingToken.ThrowIfCancellationRequested();
                    nextTimer = await UntilNextTimer(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "The sweep for due timers failed; the next one tries again."
                    );
                }
                _firstSweep.TrySetResult();

                await Wait(wake.Reader, nextTimer, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }

        await listening;
    }

    // Delivers the notices as they come, in deduplicated batches, until the next full sweep is due: the interval
    // has passed (the longer one while the notices are heard), or a full sweep was asked for.
    private async Task Wait(
        ChannelReader<string?> wake,
        TimeSpan? nextTimer,
        CancellationToken stoppingToken
    )
    {
        var since = Stopwatch.GetTimestamp();
        while (true)
        {
            var pace = _hearing ? ListeningInterval : options.InvokeOutcomeSweepInterval;
            // The next due timer wakes the sweep before the interval does.
            if (nextTimer is { } timer && timer < pace)
                pace = timer < MinTimerWake ? MinTimerWake : timer;
            var due = pace - Stopwatch.GetElapsedTime(since);
            if (due <= TimeSpan.Zero)
                return;

            // Never waits past the plain interval, so losing the notices brings the sweep back to that pace.
            using var interval = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            interval.CancelAfter(
                due < options.InvokeOutcomeSweepInterval ? due : options.InvokeOutcomeSweepInterval
            );
            try
            {
                await wake.WaitToReadAsync(interval.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                continue;
            }

            var batch = new HashSet<string>(StringComparer.Ordinal);
            var sweep = false;
            while (batch.Count < NoticeCapacity && wake.TryRead(out var notice))
                if (notice is null)
                    sweep = true;
                else
                    batch.Add(notice);

            // A full sweep finds every run the batch names.
            if (sweep || Volatile.Read(ref _sweepAsked) == 1)
                return;

            try
            {
                await DeliverAmong(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Could not read which of {Count} notified invoked runs have ended; the next sweep finds them.",
                    batch.Count
                );
            }
        }
    }

    private TimeSpan ListeningInterval =>
        options.InvokeOutcomeSweepIntervalWhileListening > options.InvokeOutcomeSweepInterval
            ? options.InvokeOutcomeSweepIntervalWhileListening
            : options.InvokeOutcomeSweepInterval;

    // Holds a subscription to the provider's notices for as long as the host runs, resubscribing after a lost
    // connection with a back-off that doubles up to its cap; each resubscription asks for a full sweep, which
    // covers whatever ended while it was not listening.
    private async Task Listen(
        IInvokedRunListener source,
        ChannelWriter<string?> wake,
        CancellationToken stoppingToken
    )
    {
        var first = true;
        var delay = ResubscribeDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var subscription = await source.SubscribeAsync(stoppingToken);
                _hearing = true;
                delay = ResubscribeDelay;
                if (!first)
                    AskForSweep(wake);
                first = false;
                _listening.TrySetResult();
                Subscribed?.Invoke();
                while (true)
                    Notice(wake, await subscription.NextAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _hearing = false;
                logger.LogWarning(
                    ex,
                    "Lost the notice of invoked runs ending; subscribing again in {Delay}. The sweep still "
                        + "delivers every outcome.",
                    delay
                );
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                delay = delay * 2 > ResubscribeDelayCap ? ResubscribeDelayCap : delay * 2;
            }
        }
    }

    // A notice the full buffer cannot take is dropped; the full sweep asked for in its place finds its run, and
    // the full buffer wakes the reader to start it.
    private void Notice(ChannelWriter<string?> wake, string token)
    {
        if (!wake.TryWrite(token))
            Interlocked.Exchange(ref _sweepAsked, 1);
    }

    private void AskForSweep(ChannelWriter<string?> wake)
    {
        Interlocked.Exchange(ref _sweepAsked, 1);
        wake.TryWrite(null);
    }
}

/// <summary>
/// The fast path: on the host that ran an invoked train, delivers its outcome as soon as its terminal write has
/// committed, when this host registers the machine that invoked it. Runs outside the train's execution context, so
/// nothing ambient from the run (its log scope, its caller) reaches the delivery. Never throws: the reconciler's
/// sweep delivers whatever this misses.
/// </summary>
internal sealed class InvokeOutcomeHookFactory(InvokeOutcomeReconciler reconciler)
    : ITrainLifecycleHookFactory
{
    // Long enough for a delivery that queues the next run; a hook never holds the run that called it longer.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    public ITrainLifecycleHook Create() => new Hook(reconciler);

    private sealed class Hook(InvokeOutcomeReconciler reconciler) : ITrainLifecycleHook
    {
        public Task OnCompleted(Metadata metadata, CancellationToken ct) => Deliver(metadata);

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            Deliver(metadata);

        public Task OnCancelled(Metadata metadata, CancellationToken ct) => Deliver(metadata);

        private async Task Deliver(Metadata metadata)
        {
            if (
                metadata.InvokingMachine is not { } machine
                || !reconciler.Handles(machine)
                || string.IsNullOrEmpty(metadata.ExternalId)
            )
                return;

            var token = metadata.ExternalId;
            try
            {
                using var bounded = new CancellationTokenSource(Bound);
                Task delivery;
                using (ExecutionContext.SuppressFlow())
                    delivery = Task.Run(() => reconciler.TryDeliver(token, bounded.Token));
                await delivery;
            }
            catch (Exception)
            {
                // TryDeliver logs its own failures; a bound that ran out leaves the run to the sweep.
            }
        }
    }
}
