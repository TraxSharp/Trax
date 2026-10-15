using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The machine-agnostic face of <see cref="SnapshotEffectRunner{TState,TTrigger}"/> (for the registry).</summary>
public interface ISnapshotEffectRunner
{
    /// <summary>
    /// Runs the machine's effect for the caller's draft at most once and commits its receipt by firing the
    /// effect's trigger. A <paramref name="requestId"/> the draft recorded for this send replays its recorded result,
    /// wherever the draft is now, and one recorded for a different trigger is <see cref="AdvanceOutcome.Rejected"/>.
    /// Otherwise a draft already in the target state replays as <see cref="AdvanceOutcome.Advanced"/> without running
    /// anything, and a draft in any state other than the effect's from-state is <see cref="AdvanceOutcome.Rejected"/>
    /// before the effect runs; an effect already pending on the draft is rejected as <c>EFFECT_IN_PROGRESS</c> until its
    /// lease passes, when this send takes it over and runs it again under the same key. Over the Trax store the effect
    /// is pending on the draft's row while it runs, nothing else writes the draft, and its receipt, the move and the end
    /// of the mark are one write. Over a store a host substituted, the claim ledger keeps the effect, and a claim whose
    /// effect already ran on other content is rejected as <c>DRAFT_CHANGED</c>.
    /// </summary>
    /// <param name="userKey">The authenticated owner of the draft; a null, empty or whitespace key is refused.</param>
    /// <param name="id">The draft id.</param>
    /// <param name="requestId">The client's idempotency key for this send; a retry with the same id replays.</param>
    /// <param name="cancellationToken">
    /// Cancels the request up to the effect. It is not passed to the effect, which gets
    /// <see cref="CancellationToken.None"/>, and once the effect has returned, its receipt and the advance that
    /// records it are written whatever the token says, so a cancelled request never leaves an effect that ran
    /// looking as if it had not.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="userKey"/> is null, empty or whitespace.</exception>
    Task<AdvanceOutcome> Run(
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// An effect runner that checks the effect-bound event's rule against the actor before it claims the effect.
/// </summary>
internal interface IActorEffectRunner
{
    /// <summary>
    /// <see cref="ISnapshotEffectRunner.Run"/> on behalf of <paramref name="actor"/>: a client the effect-bound
    /// event's rule does not admit, or any actor that is not a client, is refused as <c>FORBIDDEN</c> before the
    /// draft is read or the effect claimed.
    /// </summary>
    Task<AdvanceOutcome> Run(
        Actor actor,
        Guid id,
        string requestId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Finishes the effect pending on <paramref name="userKey"/>'s draft <paramref name="id"/> once its claimant's lease
    /// has passed: takes the mark over, runs the effect again under the same key, and records it as the answer to the
    /// request that sent it. False when nothing was finished (no such mark, its lease is live, another host took it).
    /// The send was authorized when it was made; nothing is checked against a caller here.
    /// </summary>
    Task<bool> FinishAbandoned(string userKey, Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The generic exactly-once orchestration for the ONE irreversible transition of a machine (place an
/// order, send a letter, charge a card). It closes the residual a bare concurrency token cannot: the
/// effect fires BEFORE the state write, so N truly-simultaneous requests would each deliver unless the
/// effect itself is claim-gated. This claims the intent (via <see cref="IdempotentEffect"/>) BEFORE
/// running the effect, then advances the machine authoritatively — so two concurrent runs deliver once
/// and a crash-retry replays the receipt.
///
/// <para>The flow: <c>Load -&gt; (recorded request? replay) -&gt; (already at target? replay) -&gt; (wrong state?
/// refuse before the effect)
/// -&gt; RunOnce(effect) -&gt; Advance(trigger, {receipt}) with idempotency</c>. The effect implementation
/// and the intent key are supplied by the host; everything else is mechanism.</para>
///
/// <para>Infrastructure built by <see cref="Machine{TState,TTrigger}.CreateEffectRunner"/>; not intended to be
/// constructed directly. Consumers get an <see cref="ISnapshotEffectRunner"/> from
/// <see cref="ISnapshotMachineRegistry.EffectRunner"/>.</para>
/// </summary>
/// <typeparam name="TState">The machine's state enum.</typeparam>
/// <typeparam name="TTrigger">The machine's trigger enum.</typeparam>
internal sealed class SnapshotEffectRunner<TState, TTrigger>
    : ISnapshotEffectRunner,
        IActorEffectRunner
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly SnapshotDraftService<TState, TTrigger> _drafts;
    private readonly ISnapshotEffect _effect;
    private readonly IdempotentEffect _idempotent;
    private readonly Func<string, Guid, string> _effectKey;
    private readonly string _fromState;
    private readonly string _toState;
    private readonly string _trigger;
    private readonly string _receiptKey;
    private readonly TimeSpan? _lease;

    /// <param name="drafts">The draft service that loads the draft and commits the effect's result to it.</param>
    /// <param name="effect">The side effect to run once per intent; its receipt is recorded on the draft.</param>
    /// <param name="idempotent">The exactly-once primitive that leases and fences the effect key.</param>
    /// <param name="fromState">The only state the effect may run from (e.g. Review/Preview) — enforced before the effect.</param>
    /// <param name="trigger">The trigger that commits the result (e.g. Place/Send).</param>
    /// <param name="toState">The terminal state the trigger lands in (e.g. Placed/Sent) — a draft already there replays.</param>
    /// <param name="effectKey">Produces the intent key (server-stable; names the intent, not the content).</param>
    /// <param name="receiptKey">The context key the receipt is written under by the reducer.</param>
    /// <param name="lease">How long one attempt holds the effect's lease; null uses the <see cref="IdempotentEffect"/> default.</param>
    public SnapshotEffectRunner(
        SnapshotDraftService<TState, TTrigger> drafts,
        ISnapshotEffect effect,
        IdempotentEffect idempotent,
        TState fromState,
        TTrigger trigger,
        TState toState,
        Func<string, Guid, string> effectKey,
        string receiptKey = "receipt",
        TimeSpan? lease = null
    )
    {
        _drafts = drafts;
        _effect = effect;
        _idempotent = idempotent;
        _effectKey = effectKey;
        _fromState = fromState.ToString()!;
        _toState = toState.ToString()!;
        _trigger = trigger.ToString()!;
        _receiptKey = receiptKey;
        _lease = lease;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Acts as a client holding no role: on a machine whose effect-bound event requires one, the send is refused as
    /// <c>FORBIDDEN</c>. Send through <see cref="ISnapshotDraftService.Send"/> with the caller's actor instead.
    /// </remarks>
    public Task<AdvanceOutcome> Run(
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken = default
    )
    {
        SnapshotOwner.Require(userKey);
        return Run(new Actor.Client(userKey), id, requestId, cancellationToken);
    }

    /// <summary>How long a send waits for a copy of itself that is running the effect, before answering in progress.</summary>
    internal static readonly TimeSpan InProgressWait = TimeSpan.FromSeconds(5);

    /// <inheritdoc/>
    /// <remarks>
    /// Copies of one send racing each other converge on one answer: a copy that finds the effect running waits for it
    /// (up to <see cref="InProgressWait"/>), and a copy whose commit lost to another reads the draft again, so both
    /// answer with the snapshot the effect's run recorded.
    /// </remarks>
    public async Task<AdvanceOutcome> Run(
        Actor actor,
        Guid id,
        string requestId,
        CancellationToken cancellationToken
    )
    {
        // A request id is recorded on the row, so one that could not be is refused before anything is claimed.
        if (!RequestIdRing.IsUsable(requestId))
            return new AdvanceOutcome.Rejected(
                RefusalCode.InvalidRequestId,
                $"A request id is 1 to {RequestIdRing.MaxRequestIdLength} characters and not blank."
            );
        var waitUntil = DateTimeOffset.UtcNow + InProgressWait;
        var delay = TimeSpan.FromMilliseconds(10);
        while (true)
        {
            var outcome = await RunOnce(actor, id, requestId, cancellationToken);
            switch (outcome)
            {
                // A commit that lost to a copy of this send finds the draft at the effect's target: answer with it.
                // Any other write in between stays a conflict, and the receipt is not recorded on it.
                case AdvanceOutcome.Conflict:
                    return await AtTarget(actor, id, cancellationToken) ?? outcome;
                // The draft moved between the load and the claim (a timer fired): the effect did not run, and the
                // send is answered from the draft as it is now.
                case AdvanceOutcome.Rejected moved
                    when ReferenceEquals(moved, Moved) && DateTimeOffset.UtcNow < waitUntil:
                    continue;
                case AdvanceOutcome.Rejected moved when ReferenceEquals(moved, Moved):
                    return new AdvanceOutcome.Conflict();
                case AdvanceOutcome.Rejected { Reason: EffectInProgress }
                    when DateTimeOffset.UtcNow < waitUntil:
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 200));
                    continue;
                default:
                    return outcome;
            }
        }
    }

    private const RefusalCode EffectInProgress = RefusalCode.EffectInProgress;

    // Internal to the runner, never answered: the draft moved after the load, before the effect ran. It is told
    // apart by reference, so its code is only a placeholder.
    private static readonly AdvanceOutcome.Rejected Moved = new(
        RefusalCode.Conflict,
        "The draft moved."
    );

    private sealed class DraftMovedException : Exception;

    // The draft as the effect's commit left it, when it is at the effect's target; otherwise null.
    private async Task<AdvanceOutcome?> AtTarget(
        Actor actor,
        Guid id,
        CancellationToken cancellationToken
    ) =>
        CallerRules.UserOf(actor) is { } userKey
        && await _drafts.Load(userKey, id, cancellationToken) is LoadResult.Loaded loaded
        && loaded.Snapshot.State == _toState
            ? new AdvanceOutcome.Advanced(loaded.Snapshot)
            {
                Revision = loaded.Revision,
                RowId = loaded.RowId,
                DueAt = loaded.DueAt,
            }
            : null;

    private async Task<AdvanceOutcome> RunOnce(
        Actor actor,
        Guid id,
        string requestId,
        CancellationToken cancellationToken
    )
    {
        // The effect-bound event's rule is checked before anything is read or claimed: a caller who may not send it
        // never pays for a delivery, and a send names no trigger, so the rule is the bound one's.
        if (await _drafts.Refusal(actor, _trigger, cancellationToken) is { } refused)
            return refused;
        var userKey = CallerRules.UserOf(actor)!;
        return _drafts.Marks is { } marks
            ? await RunMarked(marks, userKey, id, requestId, cancellationToken)
            : await RunLedger(userKey, id, requestId, cancellationToken);
    }

    // Over the Trax store the effect is pending on the draft's row: the send marks the row by a conditional write, runs
    // the effect, and records its receipt, the move to the target and the end of the mark in one write. While the mark
    // is there, every other write to the row is refused or waits. A mark whose lease has passed is taken over by the
    // next send of this event, which runs the effect again under the same key.
    private async Task<AdvanceOutcome> RunMarked(
        EfSnapshotStore marks,
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken
    )
    {
        switch (await _drafts.Load(userKey, id, cancellationToken))
        {
            case LoadResult.NotFound:
                return new AdvanceOutcome.NotFound();

            case LoadResult.Invalid invalid:
                return new AdvanceOutcome.LoadError(invalid.Code, invalid.Message)
                {
                    Exception = invalid.Exception,
                };

            case LoadResult.Loaded loaded:
                // A request the draft recorded replays its recorded result, wherever the draft is now, before the
                // state checks and the effect, so a retried send never runs the effect again.
                if (
                    await _drafts.RecordedAnswer(
                        userKey,
                        id,
                        requestId,
                        _trigger,
                        loaded.Snapshot,
                        cancellationToken
                    ) is
                    { } recorded
                )
                    return recorded;

                // Already effected -> replay the stored result; never run the effect twice.
                if (loaded.Snapshot.State == _toState && loaded.Effect is null)
                    return new AdvanceOutcome.Advanced(loaded.Snapshot)
                    {
                        Revision = loaded.Revision,
                        RowId = loaded.RowId,
                        DueAt = loaded.DueAt,
                    };

                var owner = DraftOwner.User(userKey);
                // The key the earlier ledger recorded claims under, before the upgrade.
                var ledgerKey = _effectKey(userKey, id);

                // An effect already pending is finished by its claimant. Once its lease has passed on the database's
                // clock, this send takes it over and runs it again under the same key.
                if (loaded.Effect is { } pending)
                {
                    if (pending.Trigger != _trigger)
                        return EffectInProgressRefusal;
                    var takeover = Guid.NewGuid();
                    if (
                        !await marks.TakeOverEffect(
                            owner,
                            _drafts.MachineId,
                            id,
                            pending.Token,
                            takeover,
                            _lease ?? SnapshotLimits.DefaultEffectLease,
                            cancellationToken
                        )
                    )
                        return EffectInProgressRefusal;
                    return await Finish(
                        marks,
                        owner,
                        id,
                        takeover,
                        loaded.Snapshot,
                        pending.Key ?? ledgerKey,
                        ledgerKey,
                        requestId,
                        ledgerReceipt: null,
                        releaseLedger: false
                    );
                }

                // Only the required state may run the effect. Refuse BEFORE the effect, so a wrong-state
                // request can't trigger a real delivery.
                if (loaded.Snapshot.State != _fromState)
                    return new AdvanceOutcome.Rejected(
                        RefusalCode.NoTransition,
                        $"Only a {_fromState} draft can run this effect."
                    );

                // A claim the earlier ledger recorded before the upgrade: one in flight within its lease is still
                // running; a completed one for this content is recorded instead of running the effect again. Any other
                // is dropped once this send's record lands.
                var ledger = (
                    await marks.LedgerClaims([ledgerKey], cancellationToken)
                ).SingleOrDefault();
                if (ledger is { Receipt: null, LeaseLive: true })
                    return EffectInProgressRefusal;
                string? ledgerReceipt = null;
                if (ledger is { Receipt: { } earlier })
                {
                    var fingerprint = SnapshotFingerprint.Of(_drafts.Serialize(loaded.Snapshot));
                    if (
                        ledger.ContentFingerprint is null
                        || string.Equals(
                            ledger.ContentFingerprint,
                            fingerprint,
                            StringComparison.Ordinal
                        )
                    )
                        ledgerReceipt = earlier;
                }

                // One intent, one key: every run of this effect, a run again after its claimant stopped included, uses
                // it, and the next send of the event mints another.
                var token = Guid.NewGuid();
                var key = $"{ledgerKey}:{token:N}";
                if (
                    !await marks.MarkEffect(
                        owner,
                        _drafts.MachineId,
                        id,
                        loaded.Token,
                        key,
                        _trigger,
                        requestId,
                        token,
                        _lease ?? SnapshotLimits.DefaultEffectLease,
                        cancellationToken
                    )
                )
                    return Moved;
                return await Finish(
                    marks,
                    owner,
                    id,
                    token,
                    loaded.Snapshot,
                    key,
                    ledgerKey,
                    requestId,
                    ledgerReceipt,
                    releaseLedger: ledger is not null
                );

            default:
                return new AdvanceOutcome.LoadError(
                    RefusalCode.InternalError,
                    "Unknown load result."
                );
        }
    }

    private static readonly AdvanceOutcome.Rejected EffectInProgressRefusal = new(
        EffectInProgress,
        "This effect is already running."
    );

    // Runs the effect the row is marked with (or takes the receipt the earlier ledger recorded) and records it. The
    // effect is not handed the request's token, and from here on nothing is cancelled: an effect that happened must be
    // recorded. An effect that throws did not happen: the mark ends and the row is as it was. A cancellation says
    // nothing about whether it happened: the mark stays until its lease passes, and the effect is then run again under
    // the same key.
    private async Task<AdvanceOutcome> Finish(
        EfSnapshotStore marks,
        DraftOwner owner,
        Guid id,
        Guid token,
        Snapshot snapshot,
        string key,
        string ledgerKey,
        string? requestId,
        string? ledgerReceipt,
        bool releaseLedger
    )
    {
        string receipt;
        if (ledgerReceipt is not null)
            receipt = ledgerReceipt;
        else
        {
            try
            {
                receipt = await _effect.Run(snapshot, key, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await marks.EndEffect(owner, _drafts.MachineId, id, token, CancellationToken.None);
                throw;
            }
            if (string.IsNullOrEmpty(receipt))
            {
                await marks.EndEffect(owner, _drafts.MachineId, id, token, CancellationToken.None);
                throw new InvalidOperationException(
                    $"The effect for '{key}' returned no receipt, so it was treated as failed. An effect must return "
                        + "a non-empty receipt once it has run."
                );
            }
        }

        var outcome = await _drafts.RecordEffect(
            owner.UserKey!,
            id,
            _trigger,
            new JsonObject { [_receiptKey] = receipt },
            requestId,
            token
        );
        if (releaseLedger && outcome is AdvanceOutcome.Advanced)
            await marks.ReleaseLedger([ledgerKey]);
        return outcome;
    }

    /// <inheritdoc/>
    public async Task<bool> FinishAbandoned(
        string userKey,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        if (_drafts.Marks is not { } marks)
            return false;
        if (
            await _drafts.Load(userKey, id, cancellationToken)
            is not LoadResult.Loaded { Effect: { } pending } loaded
        )
            return false;
        if (pending.Trigger != _trigger || loaded.Snapshot.State != _fromState)
            return false;
        var owner = DraftOwner.User(userKey);
        var takeover = Guid.NewGuid();
        if (
            !await marks.TakeOverEffect(
                owner,
                _drafts.MachineId,
                id,
                pending.Token,
                takeover,
                _lease ?? SnapshotLimits.DefaultEffectLease,
                cancellationToken
            )
        )
            return false;
        var ledgerKey = _effectKey(userKey, id);
        return await Finish(
                marks,
                owner,
                id,
                takeover,
                loaded.Snapshot,
                pending.Key ?? ledgerKey,
                ledgerKey,
                pending.RequestId,
                ledgerReceipt: null,
                releaseLedger: false
            ) is AdvanceOutcome.Advanced;
    }

    // Over a store a host substituted for the Trax store, which cannot hold a mark: the effect-claim ledger, with its
    // earlier rules.
    private async Task<AdvanceOutcome> RunLedger(
        string userKey,
        Guid id,
        string requestId,
        CancellationToken cancellationToken
    )
    {
        switch (await _drafts.Load(userKey, id, cancellationToken))
        {
            case LoadResult.NotFound:
                return new AdvanceOutcome.NotFound();

            case LoadResult.Invalid invalid:
                return new AdvanceOutcome.LoadError(invalid.Code, invalid.Message)
                {
                    Exception = invalid.Exception,
                };

            case LoadResult.Loaded loaded:
                // A request the draft recorded replays its recorded result, and an id recorded for a different
                // trigger is refused, wherever the draft is now: a timer or another server write that moved it since
                // does not turn the client's one send into a new one. Both are answered here, before the state
                // checks and the effect, so a retried send never runs the effect again and a refusal never follows a
                // real delivery.
                if (
                    await _drafts.RecordedAnswer(
                        userKey,
                        id,
                        requestId,
                        _trigger,
                        loaded.Snapshot,
                        cancellationToken
                    ) is
                    { } recorded
                )
                    return recorded;

                // Already effected -> replay the stored result; never run the effect twice.
                if (loaded.Snapshot.State == _toState)
                    return new AdvanceOutcome.Advanced(loaded.Snapshot)
                    {
                        Revision = loaded.Revision,
                        RowId = loaded.RowId,
                        DueAt = loaded.DueAt,
                    };

                // Only the required state may run the effect. Refuse BEFORE the effect, so a wrong-state
                // request can't trigger a real delivery.
                if (loaded.Snapshot.State != _fromState)
                    return new AdvanceOutcome.Rejected(
                        RefusalCode.NoTransition,
                        $"Only a {_fromState} draft can run this effect."
                    );

                // Exactly-once DELIVERY: claim the effect key BEFORE running. Two concurrent runs (or a
                // crash-retry) run the effect once and replay the receipt. The claim records the fingerprint of the
                // content the effect runs on, so its receipt is only ever replayed onto that content.
                var fingerprint = SnapshotFingerprint.Of(_drafts.Serialize(loaded.Snapshot));
                var key = _effectKey(userKey, id);
                string receipt;
                EffectOutcome effected;
                try
                {
                    effected = await _idempotent.RunOnce(
                        key,
                        fingerprint,
                        async () =>
                        {
                            // The claim is held, so a timer that has not fired yet waits for it; one that fired
                            // between the load and the claim has moved the row, and the effect must not run on the
                            // state it left. Throwing releases the claim, and the send starts over from the load.
                            if (
                                !await _drafts.StillAsLoaded(userKey, id, loaded, cancellationToken)
                            )
                                throw new DraftMovedException();
                            // The request's token stops here: a request that goes away while the effect runs must
                            // not turn an effect that happened into a cancellation.
                            return await _effect.Run(loaded.Snapshot, key, CancellationToken.None);
                        },
                        _lease,
                        cancellationToken
                    );
                }
                catch (DraftMovedException)
                {
                    return Moved;
                }
                switch (effected)
                {
                    case EffectOutcome.Ran ran:
                        receipt = ran.Receipt;
                        break;
                    case EffectOutcome.AlreadyRan already:
                        // The effect ran on content this draft no longer holds. Committing its receipt here would
                        // record it against content it was not produced for, and running the effect again would
                        // repeat it, so neither happens. A claim with no fingerprint predates the check and
                        // replays as it always did.
                        if (
                            already.ContentFingerprint is { } ranOn
                            && !string.Equals(ranOn, fingerprint, StringComparison.Ordinal)
                        )
                            return new AdvanceOutcome.Rejected(
                                RefusalCode.DraftChanged,
                                "This draft's action already ran, on content that has changed since. Restore the "
                                    + "content it ran on to record its result."
                            );
                        receipt = already.Receipt;
                        break;
                    case EffectOutcome.InProgress:
                        return new AdvanceOutcome.Rejected(
                            EffectInProgress,
                            "This effect is already running."
                        );
                    default:
                        return new AdvanceOutcome.Rejected(
                            RefusalCode.InternalError,
                            "Unknown effect outcome."
                        );
                }

                // Fold the receipt into the terminal snapshot, on the draft exactly as the effect loaded it: if it
                // was written since (an edit, a reset, a concurrent run's commit) the CAS loses (Conflict) and the
                // receipt is not recorded on content the effect did not act on. The claim keeps it, so the next
                // send replays it onto that same content rather than running the effect again. The effect has
                // happened, so this write runs on a token the caller cannot cancel: a request that goes away now
                // must still leave the draft showing what was done. The same rule as a train's outcome, in
                // docs/adr/0005-a-trains-outcome-is-recorded-on-an-uncancellable-token.md.
                return await _drafts.AdvanceAfterEffect(
                    userKey,
                    id,
                    _trigger,
                    new JsonObject { [_receiptKey] = receipt },
                    requestId,
                    loaded.Token,
                    CancellationToken.None
                );

            default:
                return new AdvanceOutcome.LoadError(
                    RefusalCode.InternalError,
                    "Unknown load result."
                );
        }
    }
}
